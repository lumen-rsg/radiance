using DaVinci.Core;
using DaVinci.Terminal;

namespace DaVinci.Components;

public sealed record ListProps : ComponentProps
{
    public IReadOnlyList<string> Items { get; init; } = [];
    public int SelectedIndex { get; init; } = -1;
    public TextStyle? ItemStyle { get; init; }
    public TextStyle? SelectedStyle { get; init; }
    public string Bullet { get; init; } = "  ";
    public string SelectedBullet { get; init; } = "  > ";
}

public sealed class ListComponent : Component
{
    public ListComponent(ComponentProps props) : base(props) { }

    public override int ComputeHeight(int availableWidth)
    {
        var listProps = (ListProps)Props;
        return listProps.Items.Count;
    }

    public override void Render(TerminalBuffer buffer)
    {
        var listProps = (ListProps)Props;
        var itemStyle = listProps.ItemStyle ?? TextStyle.Empty;
        var selectedStyle = listProps.SelectedStyle ?? new TextStyle { Bold = true, Foreground = Color.Cyan };

        for (var i = 0; i < listProps.Items.Count; i++)
        {
            var row = LayoutRect.Y + i;
            if (row >= buffer.Height) break;

            var isSelected = i == listProps.SelectedIndex;
            var style = isSelected ? selectedStyle : itemStyle;
            var bullet = isSelected ? listProps.SelectedBullet : listProps.Bullet;

            // Render bullet
            buffer.SetText(LayoutRect.X, row, bullet, isSelected ? selectedStyle : TextStyle.Empty);

            // Render item text
            var textOffset = LayoutRect.X + AnsiCodes.VisibleWidth(bullet);
            var maxWidth = LayoutRect.Width - AnsiCodes.VisibleWidth(bullet);
            var text = listProps.Items[i];

            // Truncate if needed
            if (AnsiCodes.VisibleWidth(text) > maxWidth && maxWidth > 3)
            {
                text = TruncateVisible(text, maxWidth - 3) + "...";
            }

            buffer.SetText(textOffset, row, text, style);
        }
    }

    private static string TruncateVisible(string text, int maxWidth)
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
            if (width > maxWidth)
                return text[..i];
        }

        return text;
    }
}
