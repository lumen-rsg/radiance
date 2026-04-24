namespace DaVinci.Events;

public abstract class TerminalEvent
{
    public DateTime Timestamp { get; } = DateTime.Now;
}
