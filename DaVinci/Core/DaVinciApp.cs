using DaVinci.Diff;
using DaVinci.Events;
using DaVinci.Layout;
using DaVinci.Terminal;

namespace DaVinci.Core;

public sealed class DaVinciApp : IDisposable
{
    private readonly ITerminal _terminal;
    private readonly TerminalBuffer _buffer;
    private VTree? _tree;
    private readonly Dictionary<Guid, Component> _componentInstances = [];
    private readonly EventDispatcher _eventDispatcher = new();
    private bool _running;

    public ITerminal Terminal => _terminal;
    public EventDispatcher Events => _eventDispatcher;

    public DaVinciApp(ITerminal terminal)
    {
        _terminal = terminal;
        _buffer = new TerminalBuffer(terminal);
    }

    public void Render(VNode root)
    {
        if (_tree is null)
        {
            // First render
            _tree = new VTree(root);
            MaterializeTree(root, null);

            // Compute layout
            var constraints = new LayoutConstraints
            {
                MaxWidth = _terminal.Width,
                MaxHeight = _terminal.Height
            };
            LayoutEngine.ComputeLayout(
                root.Instance!,
                new Rect { X = 0, Y = 0, Width = _terminal.Width, Height = _terminal.Height },
                constraints);

            // Call OnMount lifecycle
            CallOnMount(root);

            // Render all components
            _buffer.BeginFrame();
            RenderTree(root.Instance!, _buffer);
            _buffer.FlushAll();
        }
        else
        {
            // Re-render: diff and patch
            var diff = _tree.Update(root);
            ApplyDiff(diff);
        }
    }

    public void ForceRender()
    {
        if (_tree is null) return;

        _buffer.BeginFrame();
        RenderTree(_tree.Root.Instance!, _buffer);
        _buffer.FlushDiff();
    }

    public void Run()
    {
        _running = true;
        _terminal.HideCursor();

        try
        {
            while (_running)
            {
                ForceRender();
                Thread.Sleep(16); // ~60fps cap
            }
        }
        finally
        {
            _terminal.ShowCursor();
        }
    }

    public void Exit()
    {
        _running = false;
    }

    public void Clear()
    {
        _buffer.Clear();
        _buffer.BeginFrame();
        _terminal.Write(AnsiCodes.ClearScreen);
        _terminal.Flush();
    }

    private void MaterializeTree(VNode node, Component? parent)
    {
        var instance = (Component)Activator.CreateInstance(node.ComponentType, node.Props)!;
        node.Instance = instance;
        instance.Parent = parent;

        _componentInstances[instance.Id] = instance;

        foreach (var child in node.Children)
        {
            MaterializeTree(child, instance);
            instance.AddChild(child.Instance!);
        }
    }

    private void RenderTree(Component component, TerminalBuffer buffer)
    {
        if (component.IsDirty)
        {
            component.Render(buffer);
            component.IsDirty = false;
        }

        foreach (var child in component.Children)
        {
            RenderTree(child, buffer);
        }
    }

    private void ApplyDiff(DiffResult diff)
    {
        _buffer.BeginFrame();

        foreach (var patch in diff.Patches)
        {
            switch (patch.Type)
            {
                case PatchType.AddNode:
                    MaterializeTree(patch.NewNode!, patch.NewNode!.Instance?.Parent);
                    break;

                case PatchType.RemoveNode:
                    if (patch.OldNode?.Instance is not null)
                    {
                        patch.OldNode.Instance.OnUnmount();
                        _componentInstances.Remove(patch.OldNode.Instance.Id);
                    }
                    break;

                case PatchType.UpdateProps:
                    if (patch.OldNode?.Instance is not null && patch.NewNode is not null)
                    {
                        patch.NewNode.Instance = patch.OldNode.Instance;
                        if (patch.NewNode.Instance.ShouldUpdate(patch.NewNode.Props))
                        {
                            patch.NewNode.Instance.UpdateProps(patch.NewNode.Props);
                        }
                    }
                    break;
            }
        }

        // Recompute layout
        var constraints = new LayoutConstraints
        {
            MaxWidth = _terminal.Width,
            MaxHeight = _terminal.Height
        };
        LayoutEngine.ComputeLayout(
            _tree!.Root.Instance!,
            new Rect { X = 0, Y = 0, Width = _terminal.Width, Height = _terminal.Height },
            constraints);

        // Re-render dirty components
        RenderTree(_tree.Root.Instance!, _buffer);
        _buffer.FlushDiff();
    }

    private static void CallOnMount(VNode node)
    {
        node.Instance?.OnMount();
        foreach (var child in node.Children)
            CallOnMount(child);
    }

    public void Dispose()
    {
        _terminal.Dispose();
    }
}
