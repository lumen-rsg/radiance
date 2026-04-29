using System.Text;
using DaVinci.Terminal;
using Radiance.Interop;
using Radiance.Terminal;

namespace Radiance.Multiplexer;

/// <summary>
/// A multiplexer session: holds windows, runs the main render loop,
/// routes keyboard input, and dispatches mux commands.
/// </summary>
public sealed class MultiplexerSession : IDisposable
{
    private readonly List<MuxWindow> _windows = new();
    private readonly TerminalBuffer _buffer;
    private readonly MultiplexerRenderer _renderer;
    private readonly KeyRouter _keyRouter;
    private readonly ITerminal _terminal;

    private int _activeWindowIndex;
    private volatile bool _running;
    private bool _disposed;
    private CopyMode? _copyMode;

    // Default shell for new panes
    private string _defaultShell = Environment.GetEnvironmentVariable("SHELL") ?? "/bin/sh";

    // Frame timing
    private const int TargetFps = 30;
    private const int FrameIntervalMs = 1000 / TargetFps;

    /// <summary>Session name (shown in status bar).</summary>
    public string Name { get; }

    /// <summary>The configured prefix key label (e.g., "Ctrl+B").</summary>
    public string PrefixLabel => _keyRouter.PrefixLabel;

    /// <summary>Windows in this session.</summary>
    public IReadOnlyList<MuxWindow> Windows => _windows;

    /// <summary>Active window index.</summary>
    public int ActiveWindowIndex => _activeWindowIndex;

    /// <summary>Active window.</summary>
    public MuxWindow? ActiveWindow =>
        _windows.Count > 0 ? _windows[_activeWindowIndex] : null;

    /// <summary>Fired when the session should be detached.</summary>
    public event Action<MultiplexerSession>? OnDetach;

    public MultiplexerSession(string name, ITerminal terminal)
    {
        Name = name;
        _terminal = terminal;
        _buffer = new TerminalBuffer(terminal);
        _renderer = new MultiplexerRenderer(_buffer);
        _keyRouter = new KeyRouter(new List<MuxPane>());
        _keyRouter.OnCommand += HandleCommand;

        // Subscribe to terminal resize events
        _terminal.OnResized += HandleTerminalResize;
    }

    /// <summary>
    /// Create the initial window with a single pane running the default shell.
    /// </summary>
    public void Initialize(string? command = null)
    {
        var shell = command ?? _defaultShell;
        var usable = _renderer.UsableArea();
        var window = new MuxWindow("0", shell, usable.Height, usable.Width);
        _windows.Add(window);

        foreach (var pane in window.Panes)
            pane.OnExit += HandlePaneExit;

        UpdateKeyRouterPanes();
    }

    /// <summary>
    /// Resize the session's virtual terminal (for daemon mode).
    /// </summary>
    public void ResizeTerminal(int width, int height)
    {
        _buffer.Resize(width, height);
        if (_terminal is HeadlessTerminal headless)
            headless.Resize(width, height);
        ResizeAllWindowPanes();
    }

    /// <summary>
    /// Handle terminal resize from the ITerminal.OnResized event (foreground mode).
    /// </summary>
    private void HandleTerminalResize(Size newSize)
    {
        _buffer.Resize(newSize.Width, newSize.Height);
        ResizeAllWindowPanes();
    }

    /// <summary>
    /// Resize panes in all windows to match the current buffer dimensions.
    /// </summary>
    private void ResizeAllWindowPanes()
    {
        var usable = _renderer.UsableArea();
        foreach (var window in _windows)
        {
            var layoutRects = window.ComputeLayout(usable);
            foreach (var (paneIdx, rect) in layoutRects)
            {
                if (paneIdx < window.Panes.Count)
                {
                    var pane = window.Panes[paneIdx];
                    var innerW = Math.Max(rect.Width - 2, 1);
                    var innerH = Math.Max(rect.Height - 2, 1);
                    pane.Resize(innerH, innerW);
                }
            }
        }
    }

    /// <summary>
    /// Run the main loop: read keys, route, render at 30fps.
    /// Blocks until the session exits or is detached.
    /// </summary>
    public void Run()
    {
        _running = true;

        // Hide cursor — the multiplexer manages its own rendering
        _terminal.HideCursor();
        _terminal.Write("\x1b[?1002h"); // Enable mouse tracking (button event + drag)
        _terminal.Write("\x1b[?1006h"); // Enable SGR extended mouse

        try
        {
            // Initial full render
            RenderFrame();

            while (_running)
            {
                // Process all pending keys
                while (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    ProcessKey(key);

                    if (!_running) break;
                }

                if (!_running) break;

                // Render frame
                RenderFrame();

                // Cap frame rate
                Thread.Sleep(FrameIntervalMs);
            }
        }
        finally
        {
            // Restore terminal
            _terminal.Write("\x1b[?1006l"); // Disable SGR mouse
            _terminal.Write("\x1b[?1002l"); // Disable mouse tracking
            _terminal.ShowCursor();
            _terminal.Write("\x1b[2J\x1b[H"); // Clear screen + home cursor
            _terminal.Flush();
        }
    }

    /// <summary>
    /// Process a mouse event. Handles:
    /// - Left click: select pane under cursor
    /// - Scroll wheel: scroll scrollback in copy mode / pass to pane
    /// - Left drag on border: resize pane split
    /// </summary>
    public void ProcessMouseEvent(MouseEvent evt)
    {
        var window = ActiveWindow;
        if (window is null) return;

        var usable = _renderer.UsableArea();
        var layoutRects = window.ComputeLayout(usable);

        switch (evt.Action)
        {
            // Left click — select the pane under the cursor
            case MouseEvent.Button.Left when !evt.Drag:
                HandleMouseClick(evt, window, layoutRects);
                break;

            // Left drag — resize if on a border
            case MouseEvent.Button.Left when evt.Drag:
                HandleMouseDrag(evt, window, layoutRects);
                break;

            // Scroll wheel — pass to copy mode or pane
            case MouseEvent.Button.ScrollUp:
                HandleScroll(-3, evt, window, layoutRects);
                break;

            case MouseEvent.Button.ScrollDown:
                HandleScroll(3, evt, window, layoutRects);
                break;
        }
    }

    private void HandleMouseClick(MouseEvent evt, MuxWindow window,
        List<(int PaneIdx, Rect Rect)> layoutRects)
    {
        for (var i = 0; i < layoutRects.Count; i++)
        {
            var (paneIdx, rect) = layoutRects[i];
            if (evt.Col >= rect.X && evt.Col < rect.X + rect.Width &&
                evt.Row >= rect.Y && evt.Row < rect.Y + rect.Height)
            {
                window.ActivePaneIndex = paneIdx;
                UpdateKeyRouterPanes();
                return;
            }
        }
    }

    private void HandleMouseDrag(MouseEvent evt, MuxWindow window,
        List<(int PaneIdx, Rect Rect)> layoutRects)
    {
        // Check if drag is on a border between panes
        for (var i = 0; i < layoutRects.Count - 1; i++)
        {
            var (_, rect) = layoutRects[i];
            var (_, next) = layoutRects[i + 1];

            // Vertical border
            if (rect.Y == next.Y && next.X == rect.X + rect.Width)
            {
                var borderX = rect.X + rect.Width - 1;
                if (Math.Abs(evt.Col - borderX) <= 1)
                {
                    // Resize: adjust split ratio based on drag position
                    var layout = window.Layout;
                    if (layout is PaneLayout.Split split)
                    {
                        var newRatio = (float)(evt.Col - rect.X) / rect.Width;
                        split.Ratio = Math.Clamp(newRatio, 0.15f, 0.85f);
                        ResizeActiveWindowPanes();
                    }
                    return;
                }
            }

            // Horizontal border
            if (rect.X == next.X && next.Y == rect.Y + rect.Height)
            {
                var borderY = rect.Y + rect.Height - 1;
                if (Math.Abs(evt.Row - borderY) <= 1)
                {
                    var layout = window.Layout;
                    if (layout is PaneLayout.Split split)
                    {
                        var newRatio = (float)(evt.Row - rect.Y) / rect.Height;
                        split.Ratio = Math.Clamp(newRatio, 0.15f, 0.85f);
                        ResizeActiveWindowPanes();
                    }
                    return;
                }
            }
        }
    }

    private void HandleScroll(int lines, MouseEvent evt, MuxWindow window,
        List<(int PaneIdx, Rect Rect)> layoutRects)
    {
        // Find the pane under the cursor
        for (var i = 0; i < layoutRects.Count; i++)
        {
            var (paneIdx, rect) = layoutRects[i];
            if (evt.Col >= rect.X && evt.Col < rect.X + rect.Width &&
                evt.Row >= rect.Y && evt.Row < rect.Y + rect.Height)
            {
                // If in copy mode on this pane, scroll the view
                if (_copyMode is { Active: true } && paneIdx == window.ActivePaneIndex)
                {
                    // PageUp/Down equivalent
                    if (lines < 0)
                        for (var j = 0; j < -lines; j++)
                            _copyMode.ProcessKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
                    else
                        for (var j = 0; j < lines; j++)
                            _copyMode.ProcessKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
                }
                else
                {
                    // Send scroll events to the child process
                    var pane = window.Panes[paneIdx];
                    if (pane.IsAlive)
                    {
                        for (var j = 0; j < Math.Abs(lines); j++)
                        {
                            var seq = lines < 0 ? "\x1b[A" : "\x1b[B";
                            // Send Shift+Up/Down for scrollback
                            seq = lines < 0 ? "\x1b[1;2A" : "\x1b[1;2B";
                            pane.WriteInput(seq);
                        }
                    }
                }
                return;
            }
        }
    }

    /// <summary>
    /// Stop the session and clean up all panes.
    /// </summary>
    public void Stop()
    {
        _running = false;
    }

    /// <summary>
    /// Feed a ConsoleKeyInfo from an external source (daemon socket).
    /// </summary>
    public void FeedKey(ConsoleKeyInfo key)
    {
        ProcessKey(key);
    }

    /// <summary>
    /// Render a frame and return the ANSI output bytes.
    /// Used by MuxDaemon to capture frames for sending to clients.
    /// Requires a HeadlessTerminal.
    /// </summary>
    public byte[] RenderToBytes()
    {
        RenderFrame();

        if (_terminal is HeadlessTerminal headless)
            return headless.DrainOutput();

        return Array.Empty<byte>();
    }

    private void ProcessKey(ConsoleKeyInfo key)
    {
        // Copy mode intercepts all keys
        if (_copyMode is { Active: true })
        {
            _copyMode.ProcessKey(key);
            return;
        }

        // Route through the Ctrl+B prefix state machine
        if (_keyRouter.ProcessKey(key))
        {
            // Key consumed by the multiplexer — it was a prefix or command
            return;
        }

        // Forward to active pane's child process
        var activePane = _keyRouter.ActivePane;
        if (activePane != null && activePane.IsAlive)
        {
            var encoded = TerminalKeyEncoder.Encode(key);
            if (encoded.Length > 0)
                activePane.WriteInput(encoded);
        }
    }

    private void HandleCommand(MuxCommand cmd)
    {
        var window = ActiveWindow;
        if (window is null) return;

        switch (cmd)
        {
            case MuxCommand.CreatePane:
                HandleCreatePane(window);
                break;

            case MuxCommand.CreatePaneH:
                HandleCreatePaneH(window);
                break;

            case MuxCommand.KillPane:
                HandleKillPane(window);
                break;

            case MuxCommand.NextPane:
                window.NextPane();
                UpdateKeyRouterPanes();
                break;

            case MuxCommand.PrevPane:
                window.PrevPane();
                UpdateKeyRouterPanes();
                break;

            case MuxCommand.ZoomPane:
                window.Zoomed = !window.Zoomed;
                ResizeActiveWindowPanes();
                break;

            case MuxCommand.NextWindow:
                SwitchWindow((_activeWindowIndex + 1) % _windows.Count);
                break;

            case MuxCommand.PrevWindow:
                SwitchWindow((_activeWindowIndex - 1 + _windows.Count) % _windows.Count);
                break;

            case MuxCommand.NewWindow:
                CreateWindow();
                break;

            case MuxCommand.KillWindow:
                KillActiveWindow();
                break;

            case MuxCommand.Detach:
                OnDetach?.Invoke(this);
                _running = false;
                break;

            case MuxCommand.PaneUp:
            case MuxCommand.PaneDown:
            case MuxCommand.PaneLeft:
            case MuxCommand.PaneRight:
                // Navigate to adjacent pane
                NavigatePane(cmd);
                break;

            case MuxCommand.ResizeUp:
            case MuxCommand.ResizeDown:
            case MuxCommand.ResizeLeft:
            case MuxCommand.ResizeRight:
                ResizePane(cmd, window);
                break;

            case MuxCommand.Refresh:
                // Force a full re-render by clearing the buffer
                _buffer.Clear();
                break;

            case MuxCommand.CopyMode:
                HandleCopyMode();
                break;

            case MuxCommand.PasteBuffer:
                // Paste is not yet implemented — future: paste from clipboard buffer
                break;
        }
    }

    /// <summary>Active copy mode instance (for render overlay).</summary>
    public CopyMode? CopyModeInstance => _copyMode;

    private void HandleCopyMode()
    {
        var pane = ActiveWindow?.ActivePane;
        if (pane is null) return;

        _copyMode = new CopyMode(pane);
        _copyMode.Enter();
    }

    private void HandleCreatePane(MuxWindow window)
    {
        var usable = _renderer.UsableArea();
        window.SplitVertical(_defaultShell, usable.Height / 2, usable.Width);
        ResizeActiveWindowPanes();
        UpdateKeyRouterPanes();

        // Subscribe to new pane exit
        var newPane = window.ActivePane;
        if (newPane != null)
            newPane.OnExit += HandlePaneExit;
    }

    private void HandleCreatePaneH(MuxWindow window)
    {
        var usable = _renderer.UsableArea();
        window.SplitHorizontal(_defaultShell, usable.Height, usable.Width / 2);
        ResizeActiveWindowPanes();
        UpdateKeyRouterPanes();

        var newPane = window.ActivePane;
        if (newPane != null)
            newPane.OnExit += HandlePaneExit;
    }

    private void HandleKillPane(MuxWindow window)
    {
        var pane = window.ActivePane;
        if (pane != null)
            pane.OnExit -= HandlePaneExit;

        if (!window.KillActivePane())
        {
            // Last pane — kill the window
            KillActiveWindow();
            return;
        }

        ResizeActiveWindowPanes();
        UpdateKeyRouterPanes();
    }

    private void CreateWindow()
    {
        var usable = _renderer.UsableArea();
        var idx = _windows.Count;
        var window = new MuxWindow(idx.ToString(), _defaultShell, usable.Height, usable.Width);
        _windows.Add(window);

        foreach (var pane in window.Panes)
            pane.OnExit += HandlePaneExit;

        SwitchWindow(_windows.Count - 1);
    }

    private void KillActiveWindow()
    {
        if (_windows.Count <= 1)
        {
            // Last window — exit the session
            _running = false;
            return;
        }

        var window = _windows[_activeWindowIndex];
        foreach (var pane in window.Panes)
            pane.OnExit -= HandlePaneExit;
        window.Close();
        _windows.RemoveAt(_activeWindowIndex);

        _activeWindowIndex = Math.Min(_activeWindowIndex, _windows.Count - 1);
        UpdateKeyRouterPanes();
        ResizeActiveWindowPanes();
    }

    private void SwitchWindow(int index)
    {
        _activeWindowIndex = index;
        UpdateKeyRouterPanes();
        ResizeActiveWindowPanes();
    }

    private void NavigatePane(MuxCommand cmd)
    {
        var window = ActiveWindow;
        if (window is null || window.PaneCount <= 1) return;

        // Simple: cycle through panes based on direction
        // A proper implementation would use the layout tree for adjacency
        switch (cmd)
        {
            case MuxCommand.PaneUp:
            case MuxCommand.PaneLeft:
                window.PrevPane();
                break;
            case MuxCommand.PaneDown:
            case MuxCommand.PaneRight:
                window.NextPane();
                break;
        }

        UpdateKeyRouterPanes();
    }

    private void ResizePane(MuxCommand cmd, MuxWindow window)
    {
        if (window.PaneCount <= 1) return;

        // Walk the layout tree and adjust the relevant split ratio
        var delta = 0.05f;
        var layout = window.Layout;
        if (layout is PaneLayout.Split split)
        {
            var adjusted = cmd switch
            {
                MuxCommand.ResizeUp or MuxCommand.ResizeLeft => split.Ratio - delta,
                MuxCommand.ResizeDown or MuxCommand.ResizeRight => split.Ratio + delta,
                _ => split.Ratio
            };
            split.Ratio = Math.Clamp(adjusted, 0.1f, 0.9f);
        }

        ResizeActiveWindowPanes();
    }

    private void HandlePaneExit(MuxPane pane, int exitCode)
    {
        pane.OnExit -= HandlePaneExit;

        var window = ActiveWindow;
        if (window is null) return;

        // If this was the active pane's exit, handle it
        if (!window.KillActivePane())
        {
            // Last pane in window — kill window on UI thread
            if (_windows.Count <= 1)
                _running = false;
            else
                KillActiveWindow();
            return;
        }

        ResizeActiveWindowPanes();
        UpdateKeyRouterPanes();
    }

    private void UpdateKeyRouterPanes()
    {
        var window = ActiveWindow;
        if (window is null) return;

        // Rebuild the pane list reference in the key router
        _keyRouter.ActivePaneIndex = window.ActivePaneIndex;
        // The key router holds a reference to the same list object,
        // so changes to the window's panes are visible automatically.
        // But the router was created with an empty list — we need to
        // use the window's panes directly.

        // For now, route keys through the active window's pane list
        // via ActivePane property
    }

    private void ResizeActiveWindowPanes()
    {
        var window = ActiveWindow;
        if (window is null) return;

        var usable = _renderer.UsableArea();
        var layoutRects = window.ComputeLayout(usable);

        foreach (var (paneIdx, rect) in layoutRects)
        {
            if (paneIdx < window.Panes.Count)
            {
                var pane = window.Panes[paneIdx];
                // Subtract border space (1 cell on each side)
                var innerW = Math.Max(rect.Width - 2, 1);
                var innerH = Math.Max(rect.Height - 2, 1);
                pane.Resize(innerH, innerW);
            }
        }
    }

    private void RenderFrame()
    {
        var window = ActiveWindow;
        if (window is null) return;

        _buffer.BeginFrame();
        _buffer.Clear();

        var usable = _renderer.UsableArea();
        var layoutRects = window.ComputeLayout(usable);

        // Blit each pane's content into the terminal buffer
        for (var i = 0; i < layoutRects.Count; i++)
        {
            var (paneIdx, rect) = layoutRects[i];
            if (paneIdx >= window.Panes.Count) continue;

            var pane = window.Panes[paneIdx];
            var paneBuffer = pane.Buffer;
            var cells = paneBuffer.GetCells();

            // Offset by 1 cell for border (top-left padding)
            var innerX = rect.X + 1;
            var innerY = rect.Y + 1;
            _buffer.Blit(cells, paneBuffer.Columns, paneBuffer.Rows, innerX, innerY);
        }

        // Draw borders
        DrawPaneBorders(layoutRects, window.ActivePaneIndex);

        // Draw status bar
        RenderStatusBar();

        // Flush diff to terminal
        _buffer.FlushDiff();
    }

    private void DrawPaneBorders(List<(int PaneIdx, Rect Rect)> rects, int activePaneIdx)
    {
        for (var i = 0; i < rects.Count; i++)
        {
            var (paneIdx, rect) = rects[i];
            var isActive = paneIdx == activePaneIdx;
            DrawBorder(rect, isActive);
        }

        // Draw separators between adjacent panes
        for (var i = 0; i < rects.Count - 1; i++)
        {
            var (_, rect) = rects[i];
            var (_, next) = rects[i + 1];
            var isActiveBorder = i == activePaneIdx || i + 1 == activePaneIdx;

            // Vertical separator
            if (rect.Y == next.Y && next.X == rect.X + rect.Width)
            {
                var x = rect.X + rect.Width - 1;
                var style = isActiveBorder
                    ? new TextStyle { Foreground = Color.Green }
                    : new TextStyle { Foreground = Color.BrightBlack };

                for (var y = rect.Y; y < rect.Y + rect.Height; y++)
                    _buffer.SetCell(x, y, Cell.FromChar('│', style));
            }

            // Horizontal separator
            if (rect.X == next.X && next.Y == rect.Y + rect.Height)
            {
                var y = rect.Y + rect.Height - 1;
                var style = isActiveBorder
                    ? new TextStyle { Foreground = Color.Green }
                    : new TextStyle { Foreground = Color.BrightBlack };

                for (var x = rect.X; x < rect.X + rect.Width; x++)
                    _buffer.SetCell(x, y, Cell.FromChar('─', style));
            }
        }
    }

    private void DrawBorder(Rect r, bool isActive)
    {
        var style = isActive
            ? new TextStyle { Foreground = Color.Green }
            : new TextStyle { Foreground = Color.BrightBlack };

        // Top edge
        for (var x = r.X; x < r.X + r.Width; x++)
            _buffer.SetCell(x, r.Y, Cell.FromChar('─', style));

        // Bottom edge
        for (var x = r.X; x < r.X + r.Width; x++)
            _buffer.SetCell(x, r.Y + r.Height - 1, Cell.FromChar('─', style));

        // Left edge
        for (var y = r.Y; y < r.Y + r.Height; y++)
            _buffer.SetCell(r.X, y, Cell.FromChar('│', style));

        // Right edge
        for (var y = r.Y; y < r.Y + r.Height; y++)
            _buffer.SetCell(r.X + r.Width - 1, y, Cell.FromChar('│', style));

        // Corners
        _buffer.SetCell(r.X, r.Y, Cell.FromChar('┌', style));
        _buffer.SetCell(r.X + r.Width - 1, r.Y, Cell.FromChar('┐', style));
        _buffer.SetCell(r.X, r.Y + r.Height - 1, Cell.FromChar('└', style));
        _buffer.SetCell(r.X + r.Width - 1, r.Y + r.Height - 1, Cell.FromChar('┘', style));
    }

    private void RenderStatusBar()
    {
        var y = _buffer.Height - 1;
        var statusBarStyle = new TextStyle
        {
            Background = Color.Green,
            Foreground = Color.Black,
            Bold = true
        };
        var activeTabStyle = new TextStyle
        {
            Background = Color.BrightGreen,
            Foreground = Color.Black,
            Bold = true
        };

        // Left: session name + prefix key
        var left = $" [{Name}] {_keyRouter.PrefixLabel} ";
        _buffer.BlitLine(0, y, left, statusBarStyle);

        var x = left.Length;

        // Window tabs
        for (var i = 0; i < _windows.Count && x < _buffer.Width; i++)
        {
            var tab = $" {_windows[i].Name}:{_windows[i].PaneCount} ";
            var style = i == _activeWindowIndex ? activeTabStyle : statusBarStyle;
            _buffer.BlitLine(x, y, tab, style);
            x += tab.Length;
        }

        // Fill rest
        for (; x < _buffer.Width; x++)
            _buffer.SetCell(x, y, Cell.FromChar(' ', statusBarStyle));

        // Right: clock
        var clock = DateTime.Now.ToString("HH:mm");
        if (_buffer.Width >= clock.Length + 2)
            _buffer.BlitLine(_buffer.Width - clock.Length - 1, y, $" {clock}", statusBarStyle);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var window in _windows)
            window.Close();
        _windows.Clear();
        _terminal.Dispose();
    }
}
