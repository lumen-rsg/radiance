using DaVinci.Core;
using DaVinci.Terminal;

namespace DaVinci.Components;

public enum TextAlignment { Left, Center, Right }

public sealed record TextProps : ComponentProps
{
    public string Content { get; init; } = "";
    public TextStyle? Style { get; init; }
    public TextAlignment Alignment { get; init; } = TextAlignment.Left;
    public bool Wrap { get; init; } = true;
    public int? MaxWidth { get; init; }
}

public sealed class TextComponent : Component
{
    public TextComponent(ComponentProps props) : base(props) { }

    public override int ComputeHeight(int availableWidth)
    {
        var textProps = (TextProps)Props;
        if (string.IsNullOrEmpty(textProps.Content)) return 1;

        var width = Math.Min(textProps.MaxWidth ?? availableWidth, availableWidth);
        if (width <= 0) return 1;

        if (!textProps.Wrap) return 1;

        var lines = WordWrap(textProps.Content, width);
        return lines.Count;
    }

    public override void Render(TerminalBuffer buffer)
    {
        var textProps = (TextProps)Props;
        var style = textProps.Style ?? TextStyle.Empty;
        var width = Math.Min(textProps.MaxWidth ?? LayoutRect.Width, LayoutRect.Width);

        if (width <= 0) return;

        var lines = textProps.Wrap && !string.IsNullOrEmpty(textProps.Content)
            ? WordWrap(textProps.Content, width)
            : [textProps.Content];

        for (var i = 0; i < lines.Count && LayoutRect.Y + i < buffer.Height; i++)
        {
            var line = lines[i];
            var visibleWidth = AnsiCodes.VisibleWidth(line);

            var col = textProps.Alignment switch
            {
                TextAlignment.Center => LayoutRect.X + (width - visibleWidth) / 2,
                TextAlignment.Right => LayoutRect.X + width - visibleWidth,
                _ => LayoutRect.X
            };

            if (!string.IsNullOrEmpty(line))
                buffer.SetText(col, LayoutRect.Y + i, line, style);
        }
    }

    internal static List<string> WordWrap(string text, int maxWidth)
    {
        var lines = new List<string>();
        if (maxWidth <= 0) { lines.Add(text); return lines; }

        var currentLine = new System.Text.StringBuilder();
        var currentWidth = 0;

        foreach (var word in SplitPreservingAnsi(text))
        {
            var wordWidth = AnsiCodes.VisibleWidth(word);

            if (currentWidth > 0 && currentWidth + 1 + wordWidth > maxWidth)
            {
                lines.Add(currentLine.ToString());
                currentLine.Clear();
                currentWidth = 0;
            }

            if (currentWidth > 0)
            {
                currentLine.Append(' ');
                currentWidth++;
            }

            currentLine.Append(word);
            currentWidth += wordWidth;

            // If the word itself exceeds maxWidth, force-break it
            while (currentWidth > maxWidth && currentLine.Length > 0)
            {
                // Find a good break point
                var breakAt = maxWidth - (currentWidth - AnsiCodes.VisibleWidth(currentLine.ToString()));
                if (breakAt <= 0) breakAt = maxWidth;

                var lineStr = currentLine.ToString();
                var breakPos = FindBreakPosition(lineStr, breakAt);
                lines.Add(lineStr[..breakPos]);
                var remaining = lineStr[breakPos..].TrimStart();
                currentLine.Clear();
                currentLine.Append(remaining);
                currentWidth = AnsiCodes.VisibleWidth(remaining);
            }
        }

        if (currentLine.Length > 0)
            lines.Add(currentLine.ToString());

        return lines.Count == 0 ? [""] : lines;
    }

    private static List<string> SplitPreservingAnsi(string text)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();
        var inAnsi = false;

        foreach (var c in text)
        {
            if (c == '\x1b')
            {
                inAnsi = true;
                current.Append(c);
                continue;
            }

            if (inAnsi)
            {
                current.Append(c);
                if (c is >= '@' and <= '~')
                    inAnsi = false;
                continue;
            }

            if (c == ' ')
            {
                if (current.Length > 0)
                {
                    words.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
            words.Add(current.ToString());

        return words;
    }

    private static int FindBreakPosition(string text, int maxWidth)
    {
        var width = 0;
        var inAnsi = false;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\x1b')
            {
                inAnsi = true;
                continue;
            }

            if (inAnsi)
            {
                if (text[i] is >= '@' and <= '~')
                    inAnsi = false;
                continue;
            }

            width += Cell.GetDisplayWidth(text[i]);
            if (width >= maxWidth)
                return i + 1;
        }

        return text.Length;
    }
}
