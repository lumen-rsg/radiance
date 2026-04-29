using DaVinci.Terminal;

namespace Radiance.Multiplexer;

/// <summary>
/// Represents a rectangular area on the terminal screen.
/// </summary>
public readonly record struct Rect(int X, int Y, int Width, int Height);

/// <summary>
/// Renders the multiplexer UI: pane content, borders, and status bar.
/// Each frame, panes blit their PaneScreenBuffer content into a TerminalBuffer,
/// borders are drawn between panes, and a tmux-style status bar sits at the bottom.
/// </summary>
public sealed class MultiplexerRenderer
{
    private readonly TerminalBuffer _buffer;

    // Style constants
    private static readonly TextStyle StatusBarStyle = new()
    {
        Background = Color.Green,
        Foreground = Color.Black,
        Bold = true
    };

    private static readonly TextStyle StatusBarActiveStyle = new()
    {
        Background = Color.BrightGreen,
        Foreground = Color.Black,
        Bold = true
    };

    private static readonly TextStyle BorderStyle = new()
    {
        Foreground = Color.BrightBlack
    };

    private static readonly TextStyle ActiveBorderTopStyle = new()
    {
        Foreground = Color.Green
    };

    // Border characters
    private const char BorderH = '─';
    private const char BorderV = '│';
    private const char BorderTL = '┌';
    private const char BorderTR = '┐';
    private const char BorderBL = '└';
    private const char BorderBR = '┘';
    private const char BorderLJ = '├';
    private const char BorderRJ = '┤';
    private const char BorderTJ = '┬';
    private const char BorderBJ = '┴';
    private const char BorderX = '┼';

    // Status bar height (1 line at the bottom)
    private const int StatusBarHeight = 1;

    public MultiplexerRenderer(TerminalBuffer buffer)
    {
        _buffer = buffer;
    }

    /// <summary>
    /// Calculate the usable area (excluding status bar).
    /// </summary>
    public Rect UsableArea() => new(0, 0, _buffer.Width, _buffer.Height - StatusBarHeight);

    /// <summary>
    /// Render a full frame: pane content + borders + status bar.
    /// </summary>
    public void Render(List<MuxPane> panes, int activePaneIndex, string sessionName,
        List<(string Name, int PaneCount)> windows, int activeWindowIndex)
    {
        var usable = UsableArea();

        if (panes.Count == 0)
        {
            _buffer.Clear();
            RenderStatusBar(sessionName, windows, activeWindowIndex);
            return;
        }

        // Calculate pane rectangles
        var rects = CalculateLayout(panes.Count, usable);

        // Blit each pane's content
        for (var i = 0; i < panes.Count; i++)
        {
            var rect = rects[i];
            var paneBuffer = panes[i].Buffer;
            var cells = paneBuffer.GetCells();

            _buffer.Blit(cells, paneBuffer.Columns, paneBuffer.Rows, rect.X, rect.Y);
        }

        // Draw borders around panes
        DrawBorders(rects, activePaneIndex);

        // Draw status bar
        RenderStatusBar(sessionName, windows, activeWindowIndex);
    }

    /// <summary>
    /// Simple layout: split panes evenly within the usable area.
    /// Single pane: full area. Two panes: vertical split.
    /// Three+: vertical split with equal columns.
    /// </summary>
    internal static List<Rect> CalculateLayout(int paneCount, Rect area)
    {
        var rects = new List<Rect>(paneCount);

        if (paneCount == 1)
        {
            rects.Add(area);
            return rects;
        }

        if (paneCount == 2)
        {
            // Split vertically (side by side)
            var halfW = area.Width / 2;
            rects.Add(new Rect(area.X, area.Y, halfW, area.Height));
            rects.Add(new Rect(area.X + halfW + 1, area.Y, area.Width - halfW - 1, area.Height));
            return rects;
        }

        // 3+: vertical split into columns
        var colWidth = area.Width / paneCount;
        var remainder = area.Width - colWidth * paneCount;

        for (var i = 0; i < paneCount; i++)
        {
            var w = colWidth + (i < remainder ? 1 : 0);
            var x = area.X;
            for (var j = 0; j < i; j++)
                x += colWidth + (j < remainder ? 1 : 0) + 1; // +1 for border

            rects.Add(new Rect(x, area.Y, w, area.Height));
        }

        return rects;
    }

    private void DrawBorders(List<Rect> rects, int activePaneIndex)
    {
        for (var i = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            var isActive = i == activePaneIndex;
            DrawBox(r, isActive);
        }

        // Draw vertical separator lines between adjacent panes
        for (var i = 0; i < rects.Count - 1; i++)
        {
            var r = rects[i];
            var next = rects[i + 1];

            // If they're side by side, draw a vertical border between them
            if (r.Y == next.Y && r.Height == next.Height && next.X == r.X + r.Width + 1)
            {
                var borderX = r.X + r.Width;
                var isActiveBorder = i == activePaneIndex || i + 1 == activePaneIndex;
                var style = isActiveBorder ? ActiveBorderTopStyle : BorderStyle;

                for (var y = r.Y; y < r.Y + r.Height; y++)
                {
                    _buffer.SetCell(borderX, y, Cell.FromChar(BorderV, style));
                }
            }

            // If they're stacked, draw a horizontal border between them
            if (r.X == next.X && r.Width == next.Width && next.Y == r.Y + r.Height + 1)
            {
                var borderY = r.Y + r.Height;
                var isActiveBorder = i == activePaneIndex || i + 1 == activePaneIndex;
                var style = isActiveBorder ? ActiveBorderTopStyle : BorderStyle;

                for (var x = r.X; x < r.X + r.Width; x++)
                {
                    _buffer.SetCell(x, borderY, Cell.FromChar(BorderH, style));
                }
            }
        }
    }

    /// <summary>
    /// Draw a box outline around a rectangle (1-cell border).
    /// </summary>
    private void DrawBox(Rect r, bool isActive)
    {
        var style = isActive ? ActiveBorderTopStyle : BorderStyle;

        // Top border
        for (var x = r.X; x < r.X + r.Width; x++)
            _buffer.SetCell(x, r.Y, Cell.FromChar(BorderH, style));

        // Bottom border
        for (var x = r.X; x < r.X + r.Width; x++)
            _buffer.SetCell(x, r.Y + r.Height - 1, Cell.FromChar(BorderH, style));

        // Left border
        for (var y = r.Y; y < r.Y + r.Height; y++)
            _buffer.SetCell(r.X, y, Cell.FromChar(BorderV, style));

        // Right border
        for (var y = r.Y; y < r.Y + r.Height; y++)
            _buffer.SetCell(r.X + r.Width - 1, y, Cell.FromChar(BorderV, style));

        // Corners
        _buffer.SetCell(r.X, r.Y, Cell.FromChar(BorderTL, style));
        _buffer.SetCell(r.X + r.Width - 1, r.Y, Cell.FromChar(BorderTR, style));
        _buffer.SetCell(r.X, r.Y + r.Height - 1, Cell.FromChar(BorderBL, style));
        _buffer.SetCell(r.X + r.Width - 1, r.Y + r.Height - 1, Cell.FromChar(BorderBR, style));
    }

    /// <summary>
    /// Render the tmux-style status bar at the bottom of the screen.
    /// Format: [session-name] window0 window1 [active-window] | HH:MM
    /// </summary>
    private void RenderStatusBar(string sessionName, List<(string Name, int PaneCount)> windows,
        int activeWindowIndex)
    {
        var y = _buffer.Height - 1;

        // Left side: session name
        var leftText = $" [{sessionName}] ";
        _buffer.BlitLine(0, y, leftText, StatusBarStyle);

        // Window tabs
        var x = leftText.Length;
        for (var i = 0; i < windows.Count && x < _buffer.Width; i++)
        {
            var tab = $" {windows[i].Name}:{windows[i].PaneCount} ";
            var tabStyle = i == activeWindowIndex ? StatusBarActiveStyle : StatusBarStyle;
            _buffer.BlitLine(x, y, tab, tabStyle);
            x += tab.Length;
        }

        // Fill remaining with status bar background
        var fillStyle = StatusBarStyle;
        for (; x < _buffer.Width; x++)
            _buffer.SetCell(x, y, Cell.FromChar(' ', fillStyle));

        // Right side: clock (last 5 chars: HH:MM)
        var clock = DateTime.Now.ToString("HH:mm");
        if (_buffer.Width >= 6)
        {
            var clockStyle = StatusBarStyle;
            _buffer.BlitLine(_buffer.Width - clock.Length - 1, y, clock + " ", clockStyle);
        }
    }
}
