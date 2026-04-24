using DaVinci.Layout;
using DaVinci.Terminal;

namespace DaVinci.Core;

public abstract class Component
{
    internal Guid Id { get; } = Guid.NewGuid();

    public Lifecycle Phase { get; internal set; } = Lifecycle.Created;

    public ComponentProps Props { get; private set; }

    public ComponentState? State { get; protected set; }

    internal Component? Parent { get; set; }

    private readonly List<Component> _children = [];
    public IReadOnlyList<Component> Children => _children.AsReadOnly();

    public Rect LayoutRect { get; internal set; }

    internal bool IsDirty { get; set; } = true;

    protected Component(ComponentProps props)
    {
        Props = props;
    }

    public void UpdateProps(ComponentProps newProps)
    {
        if (!Props.Equals(newProps))
        {
            Props = newProps;
            IsDirty = true;
        }
    }

    protected void SetState(ComponentState newState)
    {
        State = newState;
        State.IncrementVersion();
        IsDirty = true;
    }

    public void AddChild(Component child)
    {
        child.Parent = this;
        _children.Add(child);
    }

    public void RemoveChild(Component child)
    {
        _children.Remove(child);
        child.Parent = null;
    }

    public void ClearChildren()
    {
        foreach (var child in _children)
            child.Parent = null;
        _children.Clear();
    }

    // Lifecycle methods
    protected internal virtual void OnMount() { }
    protected internal virtual bool ShouldUpdate(ComponentProps newProps) => true;
    protected internal virtual void OnUpdated() { }
    protected internal virtual void OnUnmount() { }

    /// <summary>
    /// Computes the height this component will occupy given the available width.
    /// Must be overridden by all leaf components.
    /// </summary>
    public virtual int ComputeHeight(int availableWidth) => Children.Count == 0 ? 1 : 0;

    /// <summary>
    /// Renders the component's visual output into the terminal buffer.
    /// Called during each frame where IsDirty is true.
    /// </summary>
    public abstract void Render(TerminalBuffer buffer);
}
