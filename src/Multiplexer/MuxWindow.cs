namespace Radiance.Multiplexer;

/// <summary>
/// A single window (tab) in the multiplexer session.
/// Holds a list of panes, a layout tree, and tracks the active pane.
/// </summary>
public sealed class MuxWindow
{
    private readonly List<MuxPane> _panes = new();
    private PaneLayout _layout;
    private int _nextPaneId;

    /// <summary>Window name (shown in the status bar tab).</summary>
    public string Name { get; set; }

    /// <summary>Index of the active pane within the panes list.</summary>
    public int ActivePaneIndex { get; set; }

    /// <summary>Whether the active pane is zoomed (fills the whole window area).</summary>
    public bool Zoomed { get; set; }

    /// <summary>The panes in this window.</summary>
    public IReadOnlyList<MuxPane> Panes => _panes;

    /// <summary>The active pane.</summary>
    public MuxPane? ActivePane =>
        _panes.Count > 0 && ActivePaneIndex >= 0 && ActivePaneIndex < _panes.Count
            ? _panes[ActivePaneIndex]
            : null;

    /// <summary>The layout tree.</summary>
    public PaneLayout Layout => _layout;

    /// <summary>Number of panes.</summary>
    public int PaneCount => _panes.Count;

    public MuxWindow(string name, string command, int rows, int cols)
    {
        Name = name;
        _nextPaneId = 0;
        var pane = CreatePane(command, rows, cols);
        _layout = new PaneLayout.Leaf(pane.PaneId);
    }

    /// <summary>
    /// Create a new pane and assign it a unique ID for layout mapping.
    /// </summary>
    private MuxPane CreatePane(string command, int rows, int cols)
    {
        var pane = new MuxPane(command, rows, cols);
        pane.PaneId = _nextPaneId++;
        _panes.Add(pane);
        return pane;
    }

    /// <summary>
    /// Split the active pane horizontally (side by side).
    /// </summary>
    public MuxPane SplitHorizontal(string command, int rows, int cols)
    {
        if (Zoomed) Zoomed = false;

        var active = ActivePane;
        if (active is null) return CreatePane(command, rows, cols);

        var newPane = new MuxPane(command, rows, cols);
        newPane.PaneId = _nextPaneId++;
        _panes.Add(newPane);

        var newLeaf = new PaneLayout.Leaf(newPane.PaneId);
        var oldLeaf = new PaneLayout.Leaf(active.PaneId);

        // Replace the active pane's leaf with a horizontal split
        _layout = _layout.ReplaceLeaf(active.PaneId,
            new PaneLayout.Split(SplitDir.Horizontal, oldLeaf, newLeaf, 0.5f));

        ActivePaneIndex = _panes.Count - 1;
        return newPane;
    }

    /// <summary>
    /// Split the active pane vertically (stacked).
    /// </summary>
    public MuxPane SplitVertical(string command, int rows, int cols)
    {
        if (Zoomed) Zoomed = false;

        var active = ActivePane;
        if (active is null) return CreatePane(command, rows, cols);

        var newPane = new MuxPane(command, rows, cols);
        newPane.PaneId = _nextPaneId++;
        _panes.Add(newPane);

        var newLeaf = new PaneLayout.Leaf(newPane.PaneId);
        var oldLeaf = new PaneLayout.Leaf(active.PaneId);

        _layout = _layout.ReplaceLeaf(active.PaneId,
            new PaneLayout.Split(SplitDir.Vertical, oldLeaf, newLeaf, 0.5f));

        ActivePaneIndex = _panes.Count - 1;
        return newPane;
    }

    /// <summary>
    /// Kill the active pane. Returns true if the window still has panes.
    /// </summary>
    public bool KillActivePane()
    {
        if (_panes.Count <= 1) return false;

        var pane = ActivePane;
        if (pane is null) return false;

        _layout = _layout.RemoveLeaf(pane.PaneId) ?? new PaneLayout.Leaf(0);
        pane.Close();
        _panes.Remove(pane);

        ActivePaneIndex = Math.Min(ActivePaneIndex, _panes.Count - 1);
        return true;
    }

    /// <summary>
    /// Cycle to the next pane.
    /// </summary>
    public void NextPane()
    {
        if (_panes.Count <= 1) return;
        ActivePaneIndex = (ActivePaneIndex + 1) % _panes.Count;
    }

    /// <summary>
    /// Cycle to the previous pane.
    /// </summary>
    public void PrevPane()
    {
        if (_panes.Count <= 1) return;
        ActivePaneIndex = (ActivePaneIndex - 1 + _panes.Count) % _panes.Count;
    }

    /// <summary>
    /// Compute pane rectangles for the given area.
    /// </summary>
    public List<(int PaneListIndex, Rect Rect)> ComputeLayout(Rect area)
    {
        if (Zoomed && ActivePane != null)
        {
            return [(ActivePaneIndex, area)];
        }

        var leafRects = _layout.ComputeRects(area);
        var result = new List<(int, Rect)>(leafRects.Count);

        foreach (var (paneId, rect) in leafRects)
        {
            // Map layout pane ID to list index
            var idx = _panes.FindIndex(p => p.PaneId == paneId);
            if (idx >= 0)
                result.Add((idx, rect));
        }

        return result;
    }

    /// <summary>
    /// Close all panes.
    /// </summary>
    public void Close()
    {
        foreach (var pane in _panes)
            pane.Close();
        _panes.Clear();
    }
}
