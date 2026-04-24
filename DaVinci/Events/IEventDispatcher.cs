namespace DaVinci.Events;

public interface IEventDispatcher
{
    void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : TerminalEvent;
    void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : TerminalEvent;
    void Dispatch<TEvent>(TEvent evt) where TEvent : TerminalEvent;
}
