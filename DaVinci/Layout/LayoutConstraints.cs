namespace DaVinci.Layout;

public sealed record LayoutConstraints
{
    public int MaxWidth { get; init; }
    public int MaxHeight { get; init; }
    public LayoutDirection Direction { get; init; } = LayoutDirection.Vertical;
    public int Gap { get; init; } = 0;
}

public enum LayoutDirection { Vertical, Horizontal }
