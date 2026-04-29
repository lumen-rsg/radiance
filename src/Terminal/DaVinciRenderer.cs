using DaVinci.Core;
using DaVinci.Terminal;

namespace Radiance.Terminal;

/// <summary>
/// Manages the DaVinci rendering lifecycle for the Radiance shell.
/// Enters alt screen buffer for rich overlay views, then restores
/// the REPL when done. The REPL scrollback is preserved.
/// </summary>
public sealed class DaVinciRenderer : IDisposable
{
    private ConsoleTerminal? _terminal;
    private DaVinciApp? _app;
    private bool _inAltScreen;

    /// <summary>
    /// Enters the alternate screen buffer, creates a fresh
    /// <see cref="ConsoleTerminal"/> and <see cref="DaVinciApp"/>.
    /// </summary>
    public void EnterFullScreen()
    {
        _terminal = new ConsoleTerminal();
        _app = new DaVinciApp(_terminal);
        Console.Write(AnsiCodes.EnterAltScreen);
        Console.Write(AnsiCodes.HideCursor);
        _inAltScreen = true;
    }

    /// <summary>
    /// Leaves the alternate screen buffer and disposes DaVinci resources.
    /// Restores cursor visibility.
    /// </summary>
    public void ExitFullScreen()
    {
        if (!_inAltScreen) return;
        Console.Write(AnsiCodes.ShowCursor);
        Console.Write(AnsiCodes.LeaveAltScreen);
        _app?.Dispose();
        _terminal?.Dispose();
        _app = null;
        _terminal = null;
        _inAltScreen = false;
    }

    /// <summary>
    /// Enters alt screen, renders a static view to the buffer, waits for
    /// any keypress, then exits alt screen. Used for informational views
    /// (welcome banner, stats, fortune, help).
    /// </summary>
    /// <param name="render">
    /// Callback receiving the buffer, terminal width, and terminal height.
    /// Write cells to the buffer; <see cref="TerminalBuffer.FlushAll"/> is called automatically.
    /// </param>
    public void ShowStaticView(Action<TerminalBuffer, int, int> render)
    {
        EnterFullScreen();
        try
        {
            var buffer = new TerminalBuffer(_terminal!);
            render(buffer, _terminal!.Width, _terminal!.Height);
            buffer.FlushAll();
            Console.ReadKey(true);
        }
        finally
        {
            ExitFullScreen();
        }
    }

    /// <summary>
    /// Enters alt screen and runs an animation loop for a fixed duration.
    /// Each frame, the callback receives the buffer and should update it.
    /// The buffer is fully flushed each frame (full repaint is faster than
    /// computing diffs for many particles).
    /// </summary>
    /// <param name="tick">
    /// Callback receiving the buffer, terminal width, terminal height, and
    /// the current frame number (starting at 0).
    /// </param>
    /// <param name="durationMs">Total animation duration in milliseconds.</param>
    /// <param name="frameDelayMs">Delay between frames in milliseconds (default 50ms).</param>
    public void RunAnimation(Action<TerminalBuffer, int, int, int> tick, int durationMs, int frameDelayMs = 50)
    {
        EnterFullScreen();
        try
        {
            var buffer = new TerminalBuffer(_terminal!);
            var w = _terminal!.Width;
            var h = _terminal.Height;
            var startTime = Environment.TickCount64;
            var frame = 0;

            while (Environment.TickCount64 - startTime < durationMs)
            {
                buffer.Clear();
                tick(buffer, w, h, frame);
                buffer.FlushAll();
                frame++;
                Thread.Sleep(frameDelayMs);
            }
        }
        finally
        {
            ExitFullScreen();
        }
    }

    public void Dispose()
    {
        ExitFullScreen();
    }
}
