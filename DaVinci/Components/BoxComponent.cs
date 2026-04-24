using DaVinci.Core;
using DaVinci.Terminal;

namespace DaVinci.Components;

public enum BoxBorderStyle { Single, Double, Rounded, Thick, None }

public sealed record BoxProps : ComponentProps
{
    public Component? Child { get; init; }
    public BoxBorderStyle BorderStyle { get; init; } = BoxBorderStyle.Rounded;
    public Color? BorderColor { get; init; }
    public TextStyle? HeaderStyle { get; init; }
    public string? Title { get; init; }
    public int Padding { get; init; } = 1;
}

public static class BoxDrawingChars
{
    public static BoxChars GetChars(BoxBorderStyle style) => style switch
    {
        BoxBorderStyle.Single => new BoxChars("│", "─", "┌", "┐", "└", "┘"),
        BoxBorderStyle.Double => new BoxChars("║", "═", "╔", "╗", "╚", "╝"),
        BoxBorderStyle.Rounded => new BoxChars("│", "─", "╭", "╮", "╰", "╯"),
        BoxBorderStyle.Thick => new BoxChars("┃", "━", "┏", "┓", "┗", "┛"),
        BoxBorderStyle.None => new BoxChars(" ", " ", " ", " ", " ", " "),
        _ => new BoxChars("│", "─", "┌", "┐", "└", "┘")
    };
}

public readonly struct BoxChars(
    string vertical, string horizontal,
    string topLeft, string topRight,
    string bottomLeft, string bottomRight)
{
    public string Vertical { get; } = vertical;
    public string Horizontal { get; } = horizontal;
    public string TopLeft { get; } = topLeft;
    public string TopRight { get; } = topRight;
    public string BottomLeft { get; } = bottomLeft;
    public string BottomRight { get; } = bottomRight;
}

public sealed class BoxComponent : Component
{
    public BoxComponent(ComponentProps props) : base(props) { }

    public override int ComputeHeight(int availableWidth)
    {
        var boxProps = (BoxProps)Props;
        var childHeight = boxProps.Child?.ComputeHeight(availableWidth - 2 - boxProps.Padding * 2) ?? 0;
        return childHeight + 2 + boxProps.Padding * 2;
    }

    public override void Render(TerminalBuffer buffer)
    {
        var boxProps = (BoxProps)Props;
        var chars = BoxDrawingChars.GetChars(boxProps.BorderStyle);
        var borderStyle = boxProps.BorderColor.HasValue
            ? new TextStyle { Foreground = boxProps.BorderColor }
            : TextStyle.Empty;

        var x = LayoutRect.X;
        var y = LayoutRect.Y;
        var w = LayoutRect.Width;
        var h = LayoutRect.Height;

        if (w < 2 || h < 2) return;

        // Top border
        buffer.SetText(x, y, chars.TopLeft, borderStyle);
        var innerWidth = w - 2;

        if (!string.IsNullOrEmpty(boxProps.Title))
        {
            var titleStyle = boxProps.HeaderStyle ?? new TextStyle { Bold = true };
            var titleText = $" {boxProps.Title} ";
            var titleWidth = AnsiCodes.VisibleWidth(titleText);
            var remaining = innerWidth - titleWidth;

            if (remaining > 0)
            {
                var leftDash = new string('─', remaining / 2);
                var rightDash = new string('─', remaining - remaining / 2);

                var topLine = leftDash;
                buffer.SetText(x + 1, y, topLine, borderStyle);
                buffer.SetText(x + 1 + leftDash.Length, y, titleText, titleStyle);
                buffer.SetText(x + 1 + leftDash.Length + titleWidth, y, rightDash, borderStyle);
            }
            else
            {
                var horizontal = new string('─', innerWidth);
                buffer.SetText(x + 1, y, horizontal, borderStyle);
            }
        }
        else
        {
            var horizontal = new string('─', innerWidth);
            buffer.SetText(x + 1, y, horizontal, borderStyle);
        }

        buffer.SetText(x + w - 1, y, chars.TopRight, borderStyle);

        // Side borders
        for (var row = 1; row < h - 1; row++)
        {
            buffer.SetText(x, y + row, chars.Vertical, borderStyle);
            buffer.SetText(x + w - 1, y + row, chars.Vertical, borderStyle);
        }

        // Bottom border
        buffer.SetText(x, y + h - 1, chars.BottomLeft, borderStyle);
        var bottomHorizontal = new string('─', innerWidth);
        buffer.SetText(x + 1, y + h - 1, bottomHorizontal, borderStyle);
        buffer.SetText(x + w - 1, y + h - 1, chars.BottomRight, borderStyle);

        // Render child in the inset area
        if (boxProps.Child is not null)
        {
            var contentRect = new Layout.Rect
            {
                X = x + 1 + boxProps.Padding,
                Y = y + 1 + boxProps.Padding,
                Width = w - 2 - boxProps.Padding * 2,
                Height = h - 2 - boxProps.Padding * 2
            };

            boxProps.Child.LayoutRect = contentRect;
            boxProps.Child.Render(buffer);
        }
    }
}
