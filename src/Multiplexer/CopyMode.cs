using System.Text;
using DaVinci.Terminal;

namespace Radiance.Multiplexer;

/// <summary>
/// Vi-like copy/scroll mode for a multiplexer pane.
/// Entered via Ctrl+B [, exited with Escape or q.
///
/// Navigation: j/k (down/up), h/l (left/right), Ctrl+u/Ctrl+d (half page),
/// Ctrl+b/Ctrl+f (full page), g/G (top/bottom), / and ? (search),
/// v (visual select), y (yank selection), n/N (next/prev search match).
/// </summary>
public sealed class CopyMode
{
    private readonly MuxPane _pane;
    private readonly PaneScreenBuffer _buffer;

    // Viewport offset into scrollback (0 = bottom/normal view)
    private int _scrollOffset;

    // Cursor position within the combined scrollback + visible buffer view
    private int _cursorRow; // 0-based, relative to viewport top
    private int _cursorCol;

    // Visual selection
    private bool _selecting;
    private int _selStartRow;
    private int _selStartCol;
    private int _selEndRow;
    private int _selEndCol;

    // Search state
    private string _searchPattern = "";
    private bool _searchForward = true;
    private int _searchMatchRow = -1;
    private int _searchMatchCol = -1;

    // Input mode within copy mode (normal vs search input)
    private bool _inSearchInput;
    private readonly StringBuilder _searchInput = new();

    /// <summary>Whether copy mode is currently active.</summary>
    public bool Active { get; private set; }

    /// <summary>Status line text to display (mode indicator, search prompt, etc.).</summary>
    public string StatusLine { get; private set; } = "";

    /// <summary>Fired when text is yanked (copied to clipboard).</summary>
    public event Action<string>? OnYank;

    /// <summary>Total lines in the combined view (scrollback + visible).</summary>
    private int TotalLines => _buffer.Scrollback.Count + _buffer.Rows;

    /// <summary>Maximum scroll offset (0 = showing the latest content).</summary>
    private int MaxScrollOffset => Math.Max(0, _buffer.Scrollback.Count);

    public CopyMode(MuxPane pane)
    {
        _pane = pane;
        _buffer = pane.Buffer;
    }

    /// <summary>
    /// Enter copy mode. Cursor starts at the top of the visible area.
    /// </summary>
    public void Enter()
    {
        Active = true;
        _scrollOffset = MaxScrollOffset; // Start at the bottom (current view)
        _cursorRow = _buffer.Rows - 1;
        _cursorCol = 0;
        _selecting = false;
        _inSearchInput = false;
        _searchInput.Clear();
        UpdateStatusLine();
    }

    /// <summary>
    /// Exit copy mode.
    /// </summary>
    public void Exit()
    {
        Active = false;
        _selecting = false;
        _inSearchInput = false;
        _searchInput.Clear();
        StatusLine = "";
    }

    /// <summary>
    /// Process a key press in copy mode. Returns true if the key was consumed.
    /// </summary>
    public bool ProcessKey(ConsoleKeyInfo key)
    {
        if (!Active) return false;

        // Search input mode
        if (_inSearchInput)
            return ProcessSearchInput(key);

        var ctrl = key.Modifiers.HasFlag(ConsoleModifiers.Control);
        var ch = char.ToLowerInvariant(key.KeyChar);

        // Exit
        if (key.Key == ConsoleKey.Escape || ch == 'q')
        {
            Exit();
            return true;
        }

        // Movement
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
            case var _ when ch == 'k' && !ctrl:
                MoveCursor(-1, 0);
                return true;
            case ConsoleKey.DownArrow:
            case var _ when ch == 'j' && !ctrl:
                MoveCursor(1, 0);
                return true;
            case ConsoleKey.LeftArrow:
            case var _ when ch == 'h' && !ctrl:
                MoveCursor(0, -1);
                return true;
            case ConsoleKey.RightArrow:
            case var _ when ch == 'l' && !ctrl:
                MoveCursor(0, 1);
                return true;
            case ConsoleKey.Home:
                _cursorCol = 0;
                UpdateStatusLine();
                return true;
            case ConsoleKey.End:
                _cursorCol = _buffer.Columns - 1;
                UpdateStatusLine();
                return true;
            case ConsoleKey.PageUp:
                PageUp(_buffer.Rows);
                return true;
            case ConsoleKey.PageDown:
                PageDown(_buffer.Rows);
                return true;
        }

        // Ctrl commands
        if (ctrl)
        {
            switch (ch)
            {
                case 'u': PageUp(_buffer.Rows / 2); return true;
                case 'd': PageDown(_buffer.Rows / 2); return true;
                case 'b': PageUp(_buffer.Rows); return true;
                case 'f': PageDown(_buffer.Rows); return true;
            }
            return true; // Consume unknown ctrl combos
        }

        // Character commands
        switch (ch)
        {
            case 'g':
                // gg = go to top, single g = go to top of scrollback
                _scrollOffset = 0;
                _cursorRow = 0;
                _cursorCol = 0;
                UpdateStatusLine();
                return true;

            case 'G':
                _scrollOffset = MaxScrollOffset;
                _cursorRow = _buffer.Rows - 1;
                _cursorCol = 0;
                UpdateStatusLine();
                return true;

            case 'v':
                _selecting = !_selecting;
                if (_selecting)
                {
                    _selStartRow = _cursorRow;
                    _selStartCol = _cursorCol;
                    _selEndRow = _cursorRow;
                    _selEndCol = _cursorCol;
                }
                UpdateStatusLine();
                return true;

            case 'y':
                YankSelection();
                Exit();
                return true;

            case '/':
                _inSearchInput = true;
                _searchForward = true;
                _searchInput.Clear();
                StatusLine = "/";
                return true;

            case '?':
                _inSearchInput = true;
                _searchForward = false;
                _searchInput.Clear();
                StatusLine = "?";
                return true;

            case 'n':
                SearchNext(1);
                return true;

            case 'N':
                SearchNext(-1);
                return true;

            case '0':
                _cursorCol = 0;
                UpdateStatusLine();
                return true;

            case '$':
                _cursorCol = _buffer.Columns - 1;
                UpdateStatusLine();
                return true;

            case 'w':
                // Word forward
                MoveWordForward();
                return true;

            case 'b':
                // Word backward
                MoveWordBackward();
                return true;
        }

        return true; // Consume all keys in copy mode
    }

    /// <summary>
    /// Get the absolute row index (scrollback + visible) for the cursor.
    /// </summary>
    private int AbsoluteCursorRow => _scrollOffset + _cursorRow;

    /// <summary>
    /// Get a cell from the combined scrollback + visible buffer view.
    /// </summary>
    public Cell GetCell(int viewRow, int col)
    {
        var absRow = _scrollOffset + viewRow;

        if (absRow < _buffer.Scrollback.Count)
        {
            var line = _buffer.Scrollback[absRow];
            return col < line.Length ? line[col] : Cell.Empty;
        }

        var bufRow = absRow - _buffer.Scrollback.Count;
        return _buffer.GetCell(col, bufRow);
    }

    /// <summary>
    /// Get a line of text from the combined view.
    /// </summary>
    public string GetLineText(int viewRow)
    {
        var sb = new StringBuilder(_buffer.Columns);
        for (var x = 0; x < _buffer.Columns; x++)
        {
            var cell = GetCell(viewRow, x);
            if (cell.Character != ' ' || sb.Length > 0)
                sb.Append(cell.Character);
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Whether a cell is part of the current selection.
    /// </summary>
    public bool IsSelected(int viewRow, int col)
    {
        if (!_selecting) return false;

        var (startRow, startCol, endRow, endCol) = NormalizeSelection();

        if (viewRow < startRow || viewRow > endRow) return false;
        if (viewRow == startRow && col < startCol) return false;
        if (viewRow == endRow && col > endCol) return false;
        return true;
    }

    /// <summary>
    /// Whether a cell is the search match highlight.
    /// </summary>
    public bool IsSearchMatch(int viewRow, int col)
    {
        return viewRow == _searchMatchRow && col >= _searchMatchCol
            && col < _searchMatchCol + _searchPattern.Length;
    }

    private void MoveCursor(int rowDelta, int colDelta)
    {
        _cursorRow = Math.Clamp(_cursorRow + rowDelta, 0, _buffer.Rows - 1);
        _cursorCol = Math.Clamp(_cursorCol + colDelta, 0, _buffer.Columns - 1);

        // Scroll if cursor hits the edge
        if (rowDelta < 0 && _cursorRow == 0 && _scrollOffset > 0)
        {
            _scrollOffset = Math.Max(0, _scrollOffset - 1);
            _cursorRow = 0;
        }
        else if (rowDelta > 0 && _cursorRow == _buffer.Rows - 1 && _scrollOffset < MaxScrollOffset)
        {
            _scrollOffset = Math.Min(MaxScrollOffset, _scrollOffset + 1);
            _cursorRow = _buffer.Rows - 1;
        }

        if (_selecting)
        {
            _selEndRow = _cursorRow;
            _selEndCol = _cursorCol;
        }

        UpdateStatusLine();
    }

    private void PageUp(int lines)
    {
        _scrollOffset = Math.Max(0, _scrollOffset - lines);
        UpdateStatusLine();
    }

    private void PageDown(int lines)
    {
        _scrollOffset = Math.Min(MaxScrollOffset, _scrollOffset + lines);
        UpdateStatusLine();
    }

    private void MoveWordForward()
    {
        var row = _cursorRow;
        var col = _cursorCol;
        var startedOnSpace = GetCell(row, col).Character == ' ';

        // Skip current word characters (or spaces if starting on space)
        while (col < _buffer.Columns)
        {
            var ch = GetCell(row, col).Character;
            if (startedOnSpace ? ch != ' ' : ch == ' ')
                break;
            col++;
        }

        // Skip spaces to find next word
        while (col < _buffer.Columns && GetCell(row, col).Character == ' ')
            col++;

        if (col >= _buffer.Columns)
            col = _buffer.Columns - 1;

        _cursorCol = col;
        if (_selecting) { _selEndRow = _cursorRow; _selEndCol = _cursorCol; }
        UpdateStatusLine();
    }

    private void MoveWordBackward()
    {
        var col = _cursorCol - 1;

        // Skip spaces
        while (col > 0 && GetCell(_cursorRow, col).Character == ' ')
            col--;

        // Skip word characters
        while (col > 0 && GetCell(_cursorRow, col - 1).Character != ' ')
            col--;

        _cursorCol = Math.Max(0, col);
        if (_selecting) { _selEndRow = _cursorRow; _selEndCol = _cursorCol; }
        UpdateStatusLine();
    }

    private bool ProcessSearchInput(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                _inSearchInput = false;
                UpdateStatusLine();
                return true;

            case ConsoleKey.Enter:
                _searchPattern = _searchInput.ToString();
                _inSearchInput = false;
                if (_searchPattern.Length > 0)
                    SearchNext(_searchForward ? 1 : -1);
                UpdateStatusLine();
                return true;

            case ConsoleKey.Backspace:
                if (_searchInput.Length > 0)
                {
                    _searchInput.Remove(_searchInput.Length - 1, 1);
                    StatusLine = (_searchForward ? "/" : "?") + _searchInput;
                }
                else
                {
                    _inSearchInput = false;
                    UpdateStatusLine();
                }
                return true;
        }

        // Append printable chars
        if (!char.IsControl(key.KeyChar))
        {
            _searchInput.Append(key.KeyChar);
            StatusLine = (_searchForward ? "/" : "?") + _searchInput;
        }

        return true;
    }

    private void SearchNext(int direction)
    {
        if (string.IsNullOrEmpty(_searchPattern)) return;

        var startRow = AbsoluteCursorRow + direction;
        var totalLines = TotalLines;

        for (var i = 0; i < totalLines; i++)
        {
            var absRow = (startRow + i * direction + totalLines) % totalLines;
            var lineText = GetLineText(absRow - _scrollOffset);
            var idx = _searchForward
                ? lineText.IndexOf(_searchPattern, StringComparison.OrdinalIgnoreCase)
                : lineText.LastIndexOf(_searchPattern, StringComparison.OrdinalIgnoreCase);

            if (idx >= 0)
            {
                // Scroll to make this row visible
                var viewRow = absRow - _scrollOffset;
                if (viewRow < 0 || viewRow >= _buffer.Rows)
                {
                    _scrollOffset = Math.Clamp(absRow - _buffer.Rows / 2, 0, MaxScrollOffset);
                }

                viewRow = absRow - _scrollOffset;
                _cursorRow = Math.Clamp(viewRow, 0, _buffer.Rows - 1);
                _cursorCol = idx;
                _searchMatchRow = viewRow;
                _searchMatchCol = idx;
                UpdateStatusLine();
                return;
            }
        }

        // No match found
        StatusLine = $"Pattern not found: {_searchPattern}";
    }

    private void YankSelection()
    {
        if (!_selecting) return;

        var (startRow, startCol, endRow, endCol) = NormalizeSelection();
        var sb = new StringBuilder();

        for (var row = startRow; row <= endRow; row++)
        {
            if (row > startRow) sb.Append('\n');

            var lineStart = row == startRow ? startCol : 0;
            var lineEnd = row == endRow ? endCol : _buffer.Columns - 1;

            for (var col = lineStart; col <= lineEnd; col++)
            {
                sb.Append(GetCell(row, col).Character);
            }
        }

        var text = sb.ToString().TrimEnd();
        if (text.Length > 0)
        {
            OnYank?.Invoke(text);
            // Try to copy to system clipboard via pbcopy (macOS)
            try
            {
                var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = "pbcopy";
                process.StartInfo.RedirectStandardInput = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                process.StandardInput.Write(text);
                process.StandardInput.Close();
                process.WaitForExit(1000);
            }
            catch { }
        }

        _selecting = false;
    }

    private (int startRow, int startCol, int endRow, int endCol) NormalizeSelection()
    {
        if (_selStartRow < _selEndRow ||
            (_selStartRow == _selEndRow && _selStartCol <= _selEndCol))
        {
            return (_selStartRow, _selStartCol, _selEndRow, _selEndCol);
        }
        return (_selEndRow, _selEndCol, _selStartRow, _selStartCol);
    }

    private void UpdateStatusLine()
    {
        var scrollPct = TotalLines > 0
            ? (int)((_scrollOffset + _buffer.Rows) / (float)TotalLines * 100)
            : 100;
        scrollPct = Math.Clamp(scrollPct, 0, 100);

        var mode = _selecting ? "-- VISUAL --" : "-- COPY --";
        StatusLine = $"[{mode}] Line {_cursorRow + 1}, Col {_cursorCol + 1}  {scrollPct}%  (scrollback: {_buffer.Scrollback.Count} lines)";
    }
}
