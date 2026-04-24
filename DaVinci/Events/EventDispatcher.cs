namespace DaVinci.Events;

public sealed class EventDispatcher : IEventDispatcher
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = [];

    public void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : TerminalEvent
    {
        var type = typeof(TEvent);
        if (!_handlers.TryGetValue(type, out var list))
            _handlers[type] = list = [];
        list.Add(handler);
    }

    public void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : TerminalEvent
    {
        if (_handlers.TryGetValue(typeof(TEvent), out var list))
            list.Remove(handler);
    }

    public void Dispatch<TEvent>(TEvent evt) where TEvent : TerminalEvent
    {
        if (_handlers.TryGetValue(typeof(TEvent), out var list))
        {
            foreach (var handler in list)
                ((Action<TEvent>)handler)(evt);
        }
    }
}
