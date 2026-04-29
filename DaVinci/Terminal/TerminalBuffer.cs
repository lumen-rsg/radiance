using System.Text;

namespace DaVinci.Terminal;

public readonly struct CellDiff(int col, int row, Cell oldCell, Cell newCell)
{
    public int Col { get; } = col;
    public int Row { get; } = row;
    public Cell OldCell { get; } = oldCell;
    public Cell NewCell { get; } = newCell;
}

public sealed class TerminalBuffer
{
    private Cell[,] _cells;
    private Cell[,] _previous;
    private readonly ITerminal _terminal;

    public int Width { get; private set; }
    public int Height { get; private set; }

    public TerminalBuffer(ITerminal terminal)
    {
        _terminal = terminal;
        Width = terminal.Width;
        Height = terminal.Height;
        _cells = CreateGrid(Width, Height);
        _previous = CreateGrid(Width, Height);
    }

    public void SetCell(int col, int row, Cell cell)
    {
        if (col < 0 || col >= Width || row < 0 || row >= Height) return;
        _cells[col, row] = cell;
    }

    public Cell GetCell(int col, int row)
    {
        if (col < 0 || col >= Width || row < 0 || row >= Height) return Cell.Empty;
        return _cells[col, row];
    }

    public void SetText(int col, int row, ReadOnlySpan<char> text, TextStyle? style)
    {
        style ??= TextStyle.Empty;
        var x = col;

        for (var i = 0; i < text.Length && x < Width; i++)
        {
            if (text[i] == '\x1b')
            {
                // Skip ANSI escape sequence
                i++;
                if (i < text.Length && text[i] == '[')
                {
                    i++;
                    while (i < text.Length)
                    {
                        var c = text[i];
                        i++;
                        if (c is >= '@' and <= '~')
                            break;
                    }
                }
                i--; // for loop will increment
                continue;
            }

            if (text[i] == '\n')
            {
                row++;
                x = col;
                continue;
            }

            if (char.IsControl(text[i])) continue;

            var cell = Cell.FromChar(text[i], style);
            SetCell(x, row, cell);

            // Wide characters take 2 cells
            x += cell.DisplayWidth;
        }
    }

    public void SetText(int col, int row, string text, TextStyle? style = null)
    {
        SetText(col, row, text.AsSpan(), style);
    }

    public void Clear()
    {
        for (var x = 0; x < Width; x++)
            for (var y = 0; y < Height; y++)
                _cells[x, y] = Cell.Empty;
    }

    public void BeginFrame()
    {
        // Snapshot current state as previous for diffing
        Array.Copy(_cells, _previous, _cells.Length);
    }

    public IReadOnlyList<CellDiff> ComputeDiff()
    {
        var diffs = new List<CellDiff>();

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var current = _cells[x, y];
                var prev = _previous[x, y];

                if (!current.Equals(prev))
                {
                    diffs.Add(new CellDiff(x, y, prev, current));
                }
            }
        }

        return diffs;
    }

    public void FlushDiff()
    {
        var diffs = ComputeDiff();
        if (diffs.Count == 0) return;

        var builder = new AnsiBuilder();

        // Group diffs by row for efficient cursor positioning
        var byRow = diffs.GroupBy(d => d.Row).OrderBy(g => g.Key);

        foreach (var rowGroup in byRow)
        {
            foreach (var diff in rowGroup.OrderBy(d => d.Col))
            {
                // Move cursor to exact position (1-based)
                builder.Move(diff.Row + 1, diff.Col + 1);

                var cell = diff.NewCell;
                if (!cell.Style.Equals(TextStyle.Empty))
                {
                    builder.ApplyStyle(cell.Style);
                    builder.Text(cell.Character);
                    builder.ResetStyle();
                }
                else
                {
                    builder.Text(cell.Character);
                }
            }
        }

        _terminal.Write(builder.ToString());
        _terminal.Flush();

        // Snapshot current state as previous for next frame
        Array.Copy(_cells, _previous, _cells.Length);
    }

    public void FlushAll()
    {
        var builder = new AnsiBuilder();
        builder.ClearScreen();

        for (var y = 0; y < Height; y++)
        {
            var cursorCol = -1; // 0-based column where the ANSI cursor currently sits

            for (var x = 0; x < Width; x++)
            {
                var cell = _cells[x, y];
                if (cell.Character == ' ' && cell.Style.Equals(TextStyle.Empty)) continue;

                // Reposition cursor when there's a gap from the last written cell
                if (cursorCol != x)
                    builder.Move(y + 1, x + 1);

                if (!cell.Style.Equals(TextStyle.Empty))
                {
                    builder.ApplyStyle(cell.Style);
                    builder.Text(cell.Character);
                    builder.ResetStyle();
                }
                else
                {
                    builder.Text(cell.Character);
                }

                cursorCol = x + cell.DisplayWidth;
            }
        }

        _terminal.Write(builder.ToString());
        _terminal.Flush();

        Array.Copy(_cells, _previous, _cells.Length);
    }

    public void Resize(int newWidth, int newHeight)
    {
        var newCells = CreateGrid(newWidth, newHeight);
        var newPrevious = CreateGrid(newWidth, newHeight);

        // Copy existing content
        var copyWidth = Math.Min(Width, newWidth);
        var copyHeight = Math.Min(Height, newHeight);

        for (var y = 0; y < copyHeight; y++)
            for (var x = 0; x < copyWidth; x++)
            {
                newCells[x, y] = _cells[x, y];
                newPrevious[x, y] = _previous[x, y];
            }

        _cells = newCells;
        _previous = newPrevious;
        Width = newWidth;
        Height = newHeight;
    }

    private static Cell[,] CreateGrid(int width, int height)
    {
        var grid = new Cell[width, height];
        for (var x = 0; x < width; x++)
            for (var y = 0; y < height; y++)
                grid[x, y] = Cell.Empty;
        return grid;
    }
}
