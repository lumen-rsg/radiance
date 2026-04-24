using DaVinci.Core;
using DaVinci.Terminal;

namespace DaVinci.Components;

public sealed record TableProps : ComponentProps
{
    public IReadOnlyList<string> Headers { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; init; } = [];
    public TextStyle? HeaderStyle { get; init; }
    public TextStyle? RowStyle { get; init; }
    public TextStyle? AltRowStyle { get; init; }
    public bool ShowBorders { get; init; } = true;
    public int[]? ColumnWidths { get; init; }
}

public sealed class TableComponent : Component
{
    public TableComponent(ComponentProps props) : base(props) { }

    public override int ComputeHeight(int availableWidth)
    {
        var tableProps = (TableProps)Props;
        var height = tableProps.Headers.Count > 0 ? 1 : 0;

        if (tableProps.ShowBorders && tableProps.Headers.Count > 0)
            height += 1; // separator line

        height += tableProps.Rows.Count;

        if (tableProps.ShowBorders)
            height += 1; // bottom border

        return height;
    }

    public override void Render(TerminalBuffer buffer)
    {
        var tableProps = (TableProps)Props;
        var headerStyle = tableProps.HeaderStyle ?? new TextStyle { Bold = true };
        var rowStyle = tableProps.RowStyle ?? TextStyle.Empty;
        var altRowStyle = tableProps.AltRowStyle ?? new TextStyle { Dim = true };

        var colCount = tableProps.Headers.Count > 0
            ? tableProps.Headers.Count
            : tableProps.Rows.Count > 0
                ? tableProps.Rows[0].Count
                : 0;

        if (colCount == 0) return;

        var colWidths = ComputeColumnWidths(tableProps, colCount, LayoutRect.Width);
        var colSeparator = tableProps.ShowBorders ? " │ " : "   ";
        var separatorWidth = AnsiCodes.VisibleWidth(colSeparator);

        var currentRow = LayoutRect.Y;

        // Render header
        if (tableProps.Headers.Count > 0)
        {
            RenderRow(buffer, currentRow, LayoutRect.X, tableProps.Headers.ToList(), colWidths, colSeparator, headerStyle);
            currentRow++;

            // Separator
            if (tableProps.ShowBorders)
            {
                var separator = BuildSeparator(colWidths, colSeparator);
                buffer.SetText(LayoutRect.X, currentRow, separator, new TextStyle { Dim = true });
                currentRow++;
            }
        }

        // Render rows
        for (var i = 0; i < tableProps.Rows.Count; i++)
        {
            if (currentRow >= buffer.Height) break;

            var style = i % 2 == 1 ? altRowStyle : rowStyle;
            var row = tableProps.Rows[i];

            // Pad row to column count
            var paddedRow = new string[colCount];
            for (var j = 0; j < colCount; j++)
                paddedRow[j] = j < row.Count ? row[j] : "";

            RenderRow(buffer, currentRow, LayoutRect.X, paddedRow, colWidths, colSeparator, style);
            currentRow++;
        }

        // Bottom border
        if (tableProps.ShowBorders)
        {
            if (currentRow < buffer.Height)
            {
                var separator = BuildSeparator(colWidths, colSeparator);
                buffer.SetText(LayoutRect.X, currentRow, separator, new TextStyle { Dim = true });
            }
        }
    }

    private int[] ComputeColumnWidths(TableProps props, int colCount, int totalWidth)
    {
        if (props.ColumnWidths is not null && props.ColumnWidths.Length == colCount)
            return props.ColumnWidths;

        var separatorWidth = props.ShowBorders ? 3 : 3; // " │ " or "   "
        var totalSeparatorWidth = (colCount - 1) * separatorWidth;
        var availableWidth = totalWidth - totalSeparatorWidth;
        var colWidth = availableWidth / colCount;

        var widths = new int[colCount];
        for (var i = 0; i < colCount; i++)
            widths[i] = colWidth;

        // Distribute remainder
        var remainder = availableWidth - colWidth * colCount;
        for (var i = 0; i < remainder; i++)
            widths[i]++;

        return widths;
    }

    private static void RenderRow(TerminalBuffer buffer, int row, int x,
        IList<string> values, int[] colWidths, string separator, TextStyle style)
    {
        var currentX = x;

        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                buffer.SetText(currentX, row, separator, new TextStyle { Dim = true });
                currentX += AnsiCodes.VisibleWidth(separator);
            }

            var text = values[i];
            var visibleWidth = AnsiCodes.VisibleWidth(text);
            var maxWidth = colWidths[i];

            if (visibleWidth > maxWidth)
            {
                text = TruncateVisible(text, maxWidth - 2) + "..";
            }
            else
            {
                text = text.PadRight(maxWidth + (text.Length - visibleWidth));
            }

            buffer.SetText(currentX, row, text, style);
            currentX += maxWidth;
        }
    }

    private static string BuildSeparator(int[] colWidths, string colSeparator)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < colWidths.Length; i++)
        {
            if (i > 0)
                sb.Append(colSeparator.Contains('│') ? "─┼─" : "───");

            sb.Append(new string('─', colWidths[i]));
        }
        return sb.ToString();
    }

    private static string TruncateVisible(string text, int maxWidth)
    {
        var width = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\x1b')
            {
                // Skip ANSI
                i++;
                while (i < text.Length && text[i] is not (>= '@' and <= '~'))
                    i++;
                continue;
            }

            width += Cell.GetDisplayWidth(text[i]);
            if (width > maxWidth)
                return text[..i];
        }
        return text;
    }
}
