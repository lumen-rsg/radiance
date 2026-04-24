using System.Text;
using System.Text.RegularExpressions;
using DaVinci.Core;
using DaVinci.Terminal;

namespace DaVinci.Components;

public sealed record MarkdownProps : ComponentProps
{
    public string Content { get; init; } = "";
    public int MaxWidth { get; init; } = 80;
}

public sealed class MarkdownComponent : Component
{
    public MarkdownComponent(ComponentProps props) : base(props) { }

    public override int ComputeHeight(int availableWidth)
    {
        var mdProps = (MarkdownProps)Props;
        var blocks = ParseBlocks(mdProps.Content);
        var height = 0;

        foreach (var block in blocks)
        {
            height += block switch
            {
                HeadingBlock h => 1 + (h.Level <= 2 ? 1 : 0), // extra line for underline
                CodeBlock => block.Lines.Count + 2,
                ListItemBlock => block.Lines.Count,
                TableBlock t => 1 + (t.Rows.Count > 0 ? 1 : 0) + t.Rows.Count + 1,
                _ => block.Lines.Count
            };
        }

        return Math.Max(1, height);
    }

    public override void Render(TerminalBuffer buffer)
    {
        var mdProps = (MarkdownProps)Props;
        var blocks = ParseBlocks(mdProps.Content);
        var y = LayoutRect.Y;
        var x = LayoutRect.X;
        var width = Math.Min(mdProps.MaxWidth, LayoutRect.Width);

        foreach (var block in blocks)
        {
            if (y >= buffer.Height) break;

            switch (block)
            {
                case HeadingBlock heading:
                    y = RenderHeading(buffer, x, y, width, heading);
                    break;
                case CodeBlock code:
                    y = RenderCodeBlock(buffer, x, y, width, code);
                    break;
                case ListItemBlock list:
                    y = RenderList(buffer, x, y, width, list);
                    break;
                case TableBlock table:
                    y = RenderTable(buffer, x, y, width, table);
                    break;
                default:
                    y = RenderParagraph(buffer, x, y, width, block);
                    break;
            }
        }
    }

    private int RenderHeading(TerminalBuffer buffer, int x, int y, int width, HeadingBlock heading)
    {
        var style = heading.Level switch
        {
            1 => new TextStyle { Bold = true, Foreground = Color.BrightCyan },
            2 => new TextStyle { Bold = true, Foreground = Color.BrightBlue },
            3 => new TextStyle { Bold = true, Foreground = Color.BrightMagenta },
            _ => new TextStyle { Bold = true }
        };

        var text = heading.Text;
        buffer.SetText(x, y, text, style);
        y++;

        if (heading.Level <= 2)
        {
            var underline = heading.Level == 1 ? new string('=', text.Length) : new string('-', text.Length);
            buffer.SetText(x, y, underline, new TextStyle { Dim = true });
            y++;
        }

        return y;
    }

    private int RenderCodeBlock(TerminalBuffer buffer, int x, int y, int width, CodeBlock code)
    {
        var borderStyle = new TextStyle { Dim = true };
        var langLabel = string.IsNullOrEmpty(code.Language) ? "" : $" {code.Language} ";
        var topBorder = "╭" + langLabel + new string('─', Math.Max(0, width - langLabel.Length - 2)) + "╮";
        buffer.SetText(x, y, topBorder, borderStyle);
        y++;

        var lineNumWidth = code.Lines.Count.ToString().Length + 1;

        for (var i = 0; i < code.Lines.Count && y < buffer.Height - 1; i++)
        {
            var lineNum = (i + 1).ToString().PadLeft(lineNumWidth);
            buffer.SetText(x, y, "│", borderStyle);
            buffer.SetText(x + 1, y, $"{lineNum} ", new TextStyle { Dim = true });

            var highlighted = CodeBlockComponent.SyntaxHighlight(code.Lines[i], code.Language);
            buffer.SetText(x + lineNumWidth + 2, y, highlighted, TextStyle.Empty);
            y++;
        }

        var bottomBorder = "╰" + new string('─', width - 2) + "╯";
        if (y < buffer.Height)
        {
            buffer.SetText(x, y, bottomBorder, borderStyle);
            y++;
        }

        return y;
    }

    private int RenderList(TerminalBuffer buffer, int x, int y, int width, ListItemBlock list)
    {
        for (var i = 0; i < list.Items.Count && y < buffer.Height; i++)
        {
            var bullet = list.Ordered ? $"{i + 1}. " : "• ";
            var itemStyle = new TextStyle { Foreground = Color.BrightYellow };
            var textStyle = TextStyle.Empty;

            buffer.SetText(x, y, bullet, itemStyle);

            var styled = RenderInlineStyles(list.Items[i]);
            buffer.SetText(x + 2, y, styled, textStyle);
            y++;
        }

        return y;
    }

    private int RenderTable(TerminalBuffer buffer, int x, int y, int width, TableBlock table)
    {
        if (table.Headers.Count == 0) return y;

        var colWidths = ComputeTableColumnWidths(table, width);

        // Header
        RenderTableRow(buffer, x, y, table.Headers, colWidths,
            new TextStyle { Bold = true, Foreground = Color.BrightCyan });
        y++;

        // Separator
        var sepLine = BuildTableSeparator(colWidths);
        buffer.SetText(x, y, sepLine, new TextStyle { Dim = true });
        y++;

        // Rows
        for (var i = 0; i < table.Rows.Count && y < buffer.Height - 1; i++)
        {
            var style = i % 2 == 1 ? new TextStyle { Dim = true } : TextStyle.Empty;
            RenderTableRow(buffer, x, y, table.Rows[i], colWidths, style);
            y++;
        }

        return y;
    }

    private int RenderParagraph(TerminalBuffer buffer, int x, int y, int width, MarkdownBlock block)
    {
        foreach (var line in block.Lines)
        {
            if (y >= buffer.Height) break;

            var styled = RenderInlineStyles(line);
            var wrapped = TextComponent.WordWrap(styled, width);

            foreach (var wrappedLine in wrapped)
            {
                if (y >= buffer.Height) break;
                buffer.SetText(x, y, wrappedLine);
                y++;
            }
        }

        return y;
    }

    private static string RenderInlineStyles(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var result = new StringBuilder();

        // Bold: **text** or __text__
        text = Regex.Replace(text, @"\*\*(.+?)\*\*", m =>
            $"{new TextStyle { Bold = true }.ToAnsiOpen()}{m.Groups[1].Value}{AnsiCodes.Reset}");

        // Italic: *text* or _text_
        text = Regex.Replace(text, @"\*(.+?)\*", m =>
            $"{new TextStyle { Italic = true }.ToAnsiOpen()}{m.Groups[1].Value}{AnsiCodes.Reset}");

        // Inline code: `text`
        text = Regex.Replace(text, @"`(.+?)`", m =>
            $"{new TextStyle { Foreground = Color.BrightGreen, Background = Color.BrightBlack }.ToAnsiOpen()}{m.Groups[1].Value}{AnsiCodes.Reset}");

        return text;
    }

    private int[] ComputeTableColumnWidths(TableBlock table, int totalWidth)
    {
        var colCount = table.Headers.Count;
        var widths = new int[colCount];
        var sepTotal = (colCount - 1) * 3;
        var available = totalWidth - sepTotal;

        // Find max content width for each column
        for (var i = 0; i < colCount; i++)
        {
            widths[i] = AnsiCodes.VisibleWidth(table.Headers[i]);
            foreach (var row in table.Rows)
            {
                if (i < row.Count)
                    widths[i] = Math.Max(widths[i], AnsiCodes.VisibleWidth(row[i]));
            }
        }

        var totalUsed = widths.Sum();
        if (totalUsed > available)
        {
            // Scale down proportionally
            var scale = (double)available / totalUsed;
            for (var i = 0; i < colCount; i++)
                widths[i] = Math.Max(4, (int)(widths[i] * scale));
        }

        return widths;
    }

    private void RenderTableRow(TerminalBuffer buffer, int x, int y,
        IList<string> values, int[] colWidths, TextStyle style)
    {
        var cx = x;
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                buffer.SetText(cx, y, " │ ", new TextStyle { Dim = true });
                cx += 3;
            }

            var text = values[i];
            var maxW = colWidths[i];
            if (AnsiCodes.VisibleWidth(text) > maxW)
                text = text[..maxW];
            else
                text = text.PadRight(maxW + (text.Length - AnsiCodes.VisibleWidth(text)));

            buffer.SetText(cx, y, text, style);
            cx += maxW;
        }
    }

    private static string BuildTableSeparator(int[] colWidths)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < colWidths.Length; i++)
        {
            if (i > 0) sb.Append("─┼─");
            sb.Append(new string('─', colWidths[i]));
        }
        return sb.ToString();
    }

    // Markdown parsing

    private static List<MarkdownBlock> ParseBlocks(string content)
    {
        var blocks = new List<MarkdownBlock>();
        var lines = content.Split('\n');
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];

            // Heading
            var headingMatch = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
            if (headingMatch.Success)
            {
                blocks.Add(new HeadingBlock(
                    headingMatch.Groups[1].Value.Length,
                    headingMatch.Groups[2].Value));
                i++;
                continue;
            }

            // Code block
            if (line.TrimStart().StartsWith("```"))
            {
                var lang = line.TrimStart()[3..].Trim();
                var codeLines = new List<string>();
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```"))
                {
                    codeLines.Add(lines[i]);
                    i++;
                }
                i++; // skip closing ```
                blocks.Add(new CodeBlock(lang, codeLines));
                continue;
            }

            // Table
            if (line.Contains('|') && line.TrimStart().StartsWith('|'))
            {
                var tableLines = new List<string>();
                while (i < lines.Length && lines[i].Contains('|'))
                {
                    tableLines.Add(lines[i]);
                    i++;
                }

                var headers = ParseTableRow(tableLines.FirstOrDefault() ?? "");
                var rows = tableLines.Skip(2) // skip header + separator
                    .Select(ParseTableRow)
                    .ToList();

                blocks.Add(new TableBlock(headers, rows));
                continue;
            }

            // List
            var listMatch = Regex.Match(line, @"^(\s*)([-*+]|\d+\.)\s+(.+)$");
            if (listMatch.Success)
            {
                var items = new List<string>();
                var ordered = Regex.IsMatch(listMatch.Groups[2].Value, @"^\d");
                items.Add(listMatch.Groups[3].Value);

                i++;
                while (i < lines.Length)
                {
                    var nextMatch = Regex.Match(lines[i], @"^(\s*)([-*+]|\d+\.)\s+(.+)$");
                    if (nextMatch.Success)
                    {
                        items.Add(nextMatch.Groups[3].Value);
                        i++;
                    }
                    else break;
                }

                blocks.Add(new ListItemBlock(ordered, items));
                continue;
            }

            // Paragraph (collect consecutive non-empty lines)
            if (!string.IsNullOrWhiteSpace(line))
            {
                var paraLines = new List<string>();
                while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) &&
                       !lines[i].TrimStart().StartsWith("#") &&
                       !lines[i].TrimStart().StartsWith("```") &&
                       !Regex.IsMatch(lines[i], @"^(\s*)([-*+]|\d+\.)\s+") &&
                       !lines[i].Contains('|'))
                {
                    paraLines.Add(lines[i]);
                    i++;
                }

                blocks.Add(new MarkdownBlock(paraLines));
                continue;
            }

            i++;
        }

        return blocks;
    }

    private static List<string> ParseTableRow(string line)
    {
        return line.Split('|')
            .Skip(1)
            .TakeWhile(s => true)
            .Select(s => s.Trim())
            .Where(s => !Regex.IsMatch(s, @"^[-:]+$"))
            .ToList();
    }
}

// Block types
internal class MarkdownBlock(List<string> lines)
{
    public List<string> Lines { get; } = lines;
}

internal class HeadingBlock(int level, string text) : MarkdownBlock([text])
{
    public int Level { get; } = level;
    public string Text { get; } = text;
}

internal class CodeBlock(string language, List<string> lines) : MarkdownBlock(lines)
{
    public string Language { get; } = language;
}

internal class ListItemBlock(bool ordered, List<string> items) : MarkdownBlock(items)
{
    public bool Ordered { get; } = ordered;
    public List<string> Items { get; } = items;
}

internal class TableBlock(List<string> headers, List<List<string>> rows) : MarkdownBlock([])
{
    public List<string> Headers { get; } = headers;
    public List<List<string>> Rows { get; } = rows;
}
