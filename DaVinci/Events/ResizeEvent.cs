namespace DaVinci.Events;

public sealed class ResizeEvent(int oldWidth, int oldHeight, int newWidth, int newHeight)
    : TerminalEvent
{
    public int OldWidth { get; } = oldWidth;
    public int OldHeight { get; } = oldHeight;
    public int NewWidth { get; } = newWidth;
    public int NewHeight { get; } = newHeight;
}
