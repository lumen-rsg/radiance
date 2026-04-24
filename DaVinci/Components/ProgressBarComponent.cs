using DaVinci.Core;
using DaVinci.Terminal;

namespace DaVinci.Components;

public sealed record ProgressBarProps : ComponentProps
{
    public double Value { get; init; }
    public double MaxValue { get; init; } = 1.0;
    public string? Label { get; init; }
    public int Width { get; init; } = 40;
    public char FillChar { get; init; } = '█';
    public char EmptyChar { get; init; } = '░';
    public Color? FillColor { get; init; }
    public bool ShowPercentage { get; init; } = true;
}

public sealed class ProgressBarComponent : Component
{
    public ProgressBarComponent(ComponentProps props) : base(props) { }

    public override int ComputeHeight(int availableWidth) => 1;

    public override void Render(TerminalBuffer buffer)
    {
        var barProps = (ProgressBarProps)Props;
        var ratio = barProps.MaxValue > 0 ? barProps.Value / barProps.MaxValue : 0;
        ratio = Math.Clamp(ratio, 0, 1);

        var percentage = $"{ratio * 100:F0}%";
        var label = barProps.Label ?? "";
        var barWidth = barProps.Width;

        // Adjust bar width for label and percentage
        var totalText = "";
        if (!string.IsNullOrEmpty(label))
            totalText += $"{label} ";
        if (barProps.ShowPercentage)
            totalText += $" {percentage}";

        var textWidth = AnsiCodes.VisibleWidth(totalText);
        var availableBarWidth = barWidth - textWidth;
        if (availableBarWidth < 4) availableBarWidth = 4;

        var filledWidth = (int)(availableBarWidth * ratio);
        var emptyWidth = availableBarWidth - filledWidth;

        var bar = new string(barProps.FillChar, filledWidth) +
                  new string(barProps.EmptyChar, emptyWidth);

        var fullLine = "";
        if (!string.IsNullOrEmpty(label))
            fullLine += $"{label} ";

        var barStyle = new TextStyle
        {
            Foreground = barProps.FillColor ?? Color.Green
        };
        var emptyStyle = new TextStyle
        {
            Dim = true,
            Foreground = barProps.FillColor ?? Color.Green
        };

        // Render label
        var x = LayoutRect.X;
        if (!string.IsNullOrEmpty(label))
        {
            buffer.SetText(x, LayoutRect.Y, $"{label} ", new TextStyle { Bold = true });
            x += label.Length + 1;
        }

        // Render filled portion
        var filledStr = new string(barProps.FillChar, filledWidth);
        buffer.SetText(x, LayoutRect.Y, filledStr, barStyle);
        x += filledWidth;

        // Render empty portion
        var emptyStr = new string(barProps.EmptyChar, emptyWidth);
        buffer.SetText(x, LayoutRect.Y, emptyStr, emptyStyle);
        x += emptyWidth;

        // Render percentage
        if (barProps.ShowPercentage)
        {
            buffer.SetText(x, LayoutRect.Y, $" {percentage}", new TextStyle { Bold = true });
        }
    }
}
