using DaVinci.Terminal;

namespace Radiance.Multiplexer;

/// <summary>
/// A virtual terminal screen buffer for a single multiplexer pane.
/// Maintains a Cell[,] grid, cursor position, scroll regions, scrollback,
/// alt-screen state, and current text style — everything needed to represent
/// what a PTY-backed child process has drawn.
/// </summary>
public sealed class PaneScreenBuffer
{
    private Cell[,] _cells;
    private Cell[,]? _altCells;
    private int _cursorCol;
    private int _cursorRow;
    private TextStyle _currentStyle = TextStyle.Empty;
    private int _scrollTop;
    private int _scrollBottom;
    private bool _inAltScreen;
    private int _savedCursorCol;
    private int _savedCursorRow;
    private TextStyle _savedStyle = TextStyle.Empty;

    /// <summary>Lines scrolled off the top of the visible area.</summary>
    private readonly List<Cell[]> _scrollback = new();

    /// <summary>Maximum scrollback lines to retain.</summary>
    private const int MaxScrollback = 10000;

    /// <summary>Whether the cursor should be visible.</summary>
    public bool CursorVisible { get; set; } = true;

    /// <summary>Window title set by the child process via OSC 0/2.</summary>
    public string Title { get; set; } = string.Empty;

    public int Columns { get; private set; }
    public int Rows { get; private set; }

    /// <summary>Current cursor column (0-based).</summary>
    public int CursorCol => _cursorCol;

    /// <summary>Current cursor row (0-based).</summary>
    public int CursorRow => _cursorRow;

    /// <summary>Scrollback lines (read-only access).</summary>
    public IReadOnlyList<Cell[]> Scrollback => _scrollback;

    /// <summary>Whether the pane is in alternate screen mode.</summary>
    public bool InAltScreen => _inAltScreen;

    /// <summary>Track if content changed since last render.</summary>
    public bool Dirty { get; private set; }

    public PaneScreenBuffer(int columns, int rows)
    {
        Columns = columns;
        Rows = rows;
        _cells = CreateGrid(columns, rows);
        _scrollTop = 0;
        _scrollBottom = rows - 1;
    }

    /// <summary>
    /// Read a cell from the grid. Returns Cell.Empty for out-of-bounds.
    /// </summary>
    public Cell GetCell(int col, int row)
    {
        if (col < 0 || col >= Columns || row < 0 || row >= Rows)
            return Cell.Empty;
        return _cells[col, row];
    }

    /// <summary>
    /// Get the entire cell grid for rendering.
    /// </summary>
    public Cell[,] GetCells() => _cells;

    /// <summary>Clear the dirty flag after rendering.</summary>
    public void ClearDirty() => Dirty = false;

    // ── Character output ─────────────────────────────────────────────

    public void WriteChar(char c)
    {
        if (_cursorCol >= Columns)
        {
            // Auto-wrap: move to next line
            _cursorCol = 0;
            _cursorRow++;
            if (_cursorRow > _scrollBottom)
            {
                ScrollUp();
                _cursorRow = _scrollBottom;
            }
        }

        var width = Cell.GetDisplayWidth(c);
        var cell = new Cell
        {
            Character = c,
            Style = _currentStyle,
            DisplayWidth = width
        };
        _cells[_cursorCol, _cursorRow] = cell;
        Dirty = true;

        // Wide char: mark the next cell as a continuation placeholder
        if (width == 2 && _cursorCol + 1 < Columns)
        {
            _cells[_cursorCol + 1, _cursorRow] = new Cell
            {
                Character = '\0',
                Style = _currentStyle,
                DisplayWidth = 0 // continuation cell
            };
        }

        _cursorCol += width;
    }

    // ── Control characters ───────────────────────────────────────────

    public void LineFeed()
    {
        if (_cursorRow == _scrollBottom)
        {
            ScrollUp();
        }
        else if (_cursorRow < Rows - 1)
        {
            _cursorRow++;
        }
        Dirty = true;
    }

    public void CarriageReturn()
    {
        _cursorCol = 0;
    }

    public void Backspace()
    {
        if (_cursorCol > 0)
            _cursorCol--;
    }

    public void Tab()
    {
        var nextTab = ((_cursorCol / 8) + 1) * 8;
        _cursorCol = Math.Min(nextTab, Columns - 1);
    }

    public void Bell()
    {
        // Could flash the border or set a flag for the renderer
    }

    // ── Cursor movement (CSI) ────────────────────────────────────────

    public void MoveCursor(int row, int col)
    {
        _cursorRow = Math.Clamp(row, 0, Rows - 1);
        _cursorCol = Math.Clamp(col, 0, Columns - 1);
    }

    public void MoveCursorUp(int n)
    {
        _cursorRow = Math.Max(_cursorRow - n, 0);
    }

    public void MoveCursorDown(int n)
    {
        _cursorRow = Math.Min(_cursorRow + n, Rows - 1);
    }

    public void MoveCursorForward(int n)
    {
        _cursorCol = Math.Min(_cursorCol + n, Columns - 1);
    }

    public void MoveCursorBackward(int n)
    {
        _cursorCol = Math.Max(_cursorCol - n, 0);
    }

    public void MoveCursorToColumn(int col)
    {
        _cursorCol = Math.Clamp(col, 0, Columns - 1);
    }

    public void MoveCursorToRow(int row)
    {
        _cursorRow = Math.Clamp(row, 0, Rows - 1);
    }

    public void SaveCursor()
    {
        _savedCursorCol = _cursorCol;
        _savedCursorRow = _cursorRow;
        _savedStyle = _currentStyle;
    }

    public void RestoreCursor()
    {
        _cursorCol = _savedCursorCol;
        _cursorRow = _savedCursorRow;
        _currentStyle = _savedStyle;
    }

    // ── Erase operations ─────────────────────────────────────────────

    /// <summary>Erase in display. mode: 0=cursor-to-end, 1=start-to-cursor, 2=all.</summary>
    public void EraseInDisplay(int mode)
    {
        switch (mode)
        {
            case 0: // Cursor to end
                // Clear from cursor to end of current line
                for (var x = _cursorCol; x < Columns; x++)
                    _cells[x, _cursorRow] = Cell.Empty;
                // Clear all lines below
                for (var y = _cursorRow + 1; y < Rows; y++)
                    ClearRow(y);
                break;
            case 1: // Start to cursor
                // Clear from start to cursor on current line
                for (var x = 0; x <= _cursorCol; x++)
                    _cells[x, _cursorRow] = Cell.Empty;
                // Clear all lines above
                for (var y = 0; y < _cursorRow; y++)
                    ClearRow(y);
                break;
            case 2: // Entire display
                for (var y = 0; y < Rows; y++)
                    ClearRow(y);
                break;
        }
        Dirty = true;
    }

    /// <summary>Erase in line. mode: 0=cursor-to-end, 1=start-to-cursor, 2=all.</summary>
    public void EraseInLine(int mode)
    {
        switch (mode)
        {
            case 0:
                for (var x = _cursorCol; x < Columns; x++)
                    _cells[x, _cursorRow] = Cell.Empty;
                break;
            case 1:
                for (var x = 0; x <= _cursorCol; x++)
                    _cells[x, _cursorRow] = Cell.Empty;
                break;
            case 2:
                ClearRow(_cursorRow);
                break;
        }
        Dirty = true;
    }

    // ── Scroll operations ────────────────────────────────────────────

    /// <summary>Set scroll region (DECSTBM). top/bottom are 0-based.</summary>
    public void SetScrollRegion(int top, int bottom)
    {
        _scrollTop = Math.Clamp(top, 0, Rows - 1);
        _scrollBottom = Math.Clamp(bottom, 0, Rows - 1);
    }

    /// <summary>Scroll the region up by n lines (content moves up, blank lines appear at bottom).</summary>
    public void ScrollUp(int n = 1)
    {
        for (var i = 0; i < n; i++)
        {
            // Save top line to scrollback
            if (_scrollTop == 0)
            {
                var line = new Cell[Columns];
                for (var x = 0; x < Columns; x++)
                    line[x] = _cells[x, 0];
                _scrollback.Add(line);
                if (_scrollback.Count > MaxScrollback)
                    _scrollback.RemoveAt(0);
            }

            // Shift rows up within the scroll region
            for (var y = _scrollTop; y < _scrollBottom; y++)
            {
                for (var x = 0; x < Columns; x++)
                    _cells[x, y] = _cells[x, y + 1];
            }

            // Clear bottom row
            ClearRow(_scrollBottom);
        }
        Dirty = true;
    }

    /// <summary>Scroll the region down by n lines (content moves down, blank lines appear at top).</summary>
    public void ScrollDown(int n = 1)
    {
        for (var i = 0; i < n; i++)
        {
            // Shift rows down within the scroll region
            for (var y = _scrollBottom; y > _scrollTop; y--)
            {
                for (var x = 0; x < Columns; x++)
                    _cells[x, y] = _cells[x, y - 1];
            }

            // Clear top row of region
            ClearRow(_scrollTop);
        }
        Dirty = true;
    }

    /// <summary>Insert n blank lines at cursor row, pushing content down within scroll region.</summary>
    public void InsertLines(int n)
    {
        if (_cursorRow < _scrollTop || _cursorRow > _scrollBottom) return;

        for (var i = 0; i < n && _scrollBottom - i >= _cursorRow; i++)
        {
            for (var y = _scrollBottom; y > _cursorRow; y--)
            {
                for (var x = 0; x < Columns; x++)
                    _cells[x, y] = _cells[x, y - 1];
            }
            ClearRow(_cursorRow);
        }
        Dirty = true;
    }

    /// <summary>Delete n lines at cursor row, pulling content up within scroll region.</summary>
    public void DeleteLines(int n)
    {
        if (_cursorRow < _scrollTop || _cursorRow > _scrollBottom) return;

        for (var i = 0; i < n; i++)
        {
            for (var y = _cursorRow; y < _scrollBottom; y++)
            {
                for (var x = 0; x < Columns; x++)
                    _cells[x, y] = _cells[x, y + 1];
            }
            ClearRow(_scrollBottom);
        }
        Dirty = true;
    }

    /// <summary>Insert n blank characters at cursor position, shifting right.</summary>
    public void InsertChars(int n)
    {
        for (var i = 0; i < n; i++)
        {
            // Shift cells right from the end
            for (var x = Columns - 1; x > _cursorCol; x--)
                _cells[x, _cursorRow] = _cells[x - 1, _cursorRow];
            _cells[_cursorCol, _cursorRow] = Cell.Empty;
        }
        Dirty = true;
    }

    /// <summary>Delete n characters at cursor position, shifting left.</summary>
    public void DeleteChars(int n)
    {
        for (var i = 0; i < n; i++)
        {
            for (var x = _cursorCol; x < Columns - 1; x++)
                _cells[x, _cursorRow] = _cells[x + 1, _cursorRow];
            _cells[Columns - 1, _cursorRow] = Cell.Empty;
        }
        Dirty = true;
    }

    // ── Style (SGR) ──────────────────────────────────────────────────

    public void SetStyle(TextStyle style)
    {
        _currentStyle = style;
    }

    /// <summary>
    /// Apply an SGR parameter list to the current style.
    /// Handles: reset (0), bold(1), dim(2), italic(3), underline(4),
    /// blink(5), reverse(7), strikethrough(9), fg/bg colors (30-37,40-47,
    /// 90-97,100-107, 38/48 extended), and reset individual attributes.
    /// </summary>
    public void ApplySgr(ReadOnlySpan<int> parameters)
    {
        if (parameters.Length == 0)
        {
            _currentStyle = TextStyle.Empty;
            return;
        }

        var i = 0;
        while (i < parameters.Length)
        {
            var p = parameters[i];

            switch (p)
            {
                case 0: // Reset
                    _currentStyle = TextStyle.Empty;
                    break;
                case 1: // Bold
                    _currentStyle = _currentStyle with { Bold = true };
                    break;
                case 2: // Dim
                    _currentStyle = _currentStyle with { Dim = true };
                    break;
                case 3: // Italic
                    _currentStyle = _currentStyle with { Italic = true };
                    break;
                case 4: // Underline
                    _currentStyle = _currentStyle with { Underline = true };
                    break;
                case 5: // Blink (slow)
                    _currentStyle = _currentStyle with { Blink = true };
                    break;
                case 7: // Reverse
                    _currentStyle = _currentStyle with { Reverse = true };
                    break;
                case 9: // Strikethrough
                    _currentStyle = _currentStyle with { Strikethrough = true };
                    break;
                case 22: // Not bold/dim
                    _currentStyle = _currentStyle with { Bold = false, Dim = false };
                    break;
                case 23: // Not italic
                    _currentStyle = _currentStyle with { Italic = false };
                    break;
                case 24: // Not underline
                    _currentStyle = _currentStyle with { Underline = false };
                    break;
                case 25: // Not blink
                    _currentStyle = _currentStyle with { Blink = false };
                    break;
                case 27: // Not reverse
                    _currentStyle = _currentStyle with { Reverse = false };
                    break;
                case 29: // Not strikethrough
                    _currentStyle = _currentStyle with { Strikethrough = false };
                    break;

                // Standard foreground colors (30-37)
                case >= 30 and <= 37:
                    _currentStyle = _currentStyle with
                    {
                        Foreground = Color.FromNamed((AnsiNamedColor)p)
                    };
                    break;

                // Standard background colors (40-47)
                case >= 40 and <= 47:
                    _currentStyle = _currentStyle with
                    {
                        Background = Color.FromNamed((AnsiNamedColor)(p - 10))
                    };
                    break;

                case 38: // Extended foreground
                {
                    var color = ParseExtendedColor(parameters, ref i);
                    if (color.HasValue)
                        _currentStyle = _currentStyle with { Foreground = color.Value };
                    break;
                }
                case 39: // Default foreground
                    _currentStyle = _currentStyle with { Foreground = null };
                    break;

                case 48: // Extended background
                {
                    var color = ParseExtendedColor(parameters, ref i);
                    if (color.HasValue)
                        _currentStyle = _currentStyle with { Background = color.Value };
                    break;
                }
                case 49: // Default background
                    _currentStyle = _currentStyle with { Background = null };
                    break;

                // Bright foreground (90-97)
                case >= 90 and <= 97:
                    _currentStyle = _currentStyle with
                    {
                        Foreground = Color.FromNamed((AnsiNamedColor)p)
                    };
                    break;

                // Bright background (100-107)
                case >= 100 and <= 107:
                    _currentStyle = _currentStyle with
                    {
                        Background = Color.FromNamed((AnsiNamedColor)(p - 10))
                    };
                    break;
            }

            i++;
        }
    }

    private static Color? ParseExtendedColor(ReadOnlySpan<int> parameters, ref int i)
    {
        if (i + 1 >= parameters.Length) return null;

        var type = parameters[i + 1];
        switch (type)
        {
            case 5: // 256-color
                if (i + 2 < parameters.Length)
                {
                    i += 2;
                    return Color.FromIndex((byte)parameters[i]);
                }
                return null;

            case 2: // Truecolor RGB
                if (i + 4 < parameters.Length)
                {
                    i += 4;
                    return Color.FromRgb(parameters[i - 2], parameters[i - 1], parameters[i]);
                }
                return null;

            default:
                return null;
        }
    }

    // ── Alt-screen buffer ────────────────────────────────────────────

    public void EnterAltScreen()
    {
        if (_inAltScreen) return;
        _altCells = _cells;
        _cells = CreateGrid(Columns, Rows);
        _inAltScreen = true;
        _cursorCol = 0;
        _cursorRow = 0;
        _scrollTop = 0;
        _scrollBottom = Rows - 1;
        Dirty = true;
    }

    public void LeaveAltScreen()
    {
        if (!_inAltScreen) return;
        _cells = _altCells!;
        _altCells = null;
        _inAltScreen = false;
        _cursorCol = 0;
        _cursorRow = 0;
        _scrollTop = 0;
        _scrollBottom = Rows - 1;
        Dirty = true;
    }

    // ── Resize ───────────────────────────────────────────────────────

    public void Resize(int newColumns, int newRows)
    {
        var newCells = CreateGrid(newColumns, newRows);

        // Copy existing content that fits
        var copyCols = Math.Min(Columns, newColumns);
        var copyRows = Math.Min(Rows, newRows);
        for (var y = 0; y < copyRows; y++)
            for (var x = 0; x < copyCols; x++)
                newCells[x, y] = _cells[x, y];

        _cells = newCells;
        Columns = newColumns;
        Rows = newRows;

        // Clamp cursor and scroll region
        _cursorCol = Math.Min(_cursorCol, newColumns - 1);
        _cursorRow = Math.Min(_cursorRow, newRows - 1);
        _scrollTop = 0;
        _scrollBottom = newRows - 1;

        Dirty = true;
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private void ClearRow(int row)
    {
        for (var x = 0; x < Columns; x++)
            _cells[x, row] = Cell.Empty;
    }

    private static Cell[,] CreateGrid(int columns, int rows)
    {
        var grid = new Cell[columns, rows];
        for (var x = 0; x < columns; x++)
            for (var y = 0; y < rows; y++)
                grid[x, y] = Cell.Empty;
        return grid;
    }
}
