using DaVinci.Terminal;

namespace Radiance.Terminal;

/// <summary>
/// Concrete <see cref="ITerminal"/> implementation wrapping <see cref="System.Console"/>.
/// Provides resize detection via polling and cursor state management.
/// </summary>
public sealed class ConsoleTerminal : ITerminal
{
    private readonly Timer _resizeTimer;
    private int _cachedWidth;
    private int _cachedHeight;
    private bool _disposed;

    public int Width => _cachedWidth;
    public int Height => _cachedHeight;
    public int CursorLeft => Console.CursorLeft;
    public int CursorTop => Console.CursorTop;
    public bool IsOutputRedirected => Console.IsOutputRedirected;

    public event Action<Size>? OnResized;

    public ConsoleTerminal()
    {
        _cachedWidth = Console.WindowWidth > 0 ? Console.WindowWidth : 80;
        _cachedHeight = Console.WindowHeight > 0 ? Console.WindowHeight : 24;
        _resizeTimer = new Timer(CheckResize, null, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
    }

    public void Write(string text) => Console.Write(text);
    public void WriteLine() => Console.WriteLine();

    public void SetCursorPosition(int left, int top)
    {
        if (!Console.IsOutputRedirected)
            Console.SetCursorPosition(left, top);
    }

    public void HideCursor() => Console.Write("\x1b[?25l");
    public void ShowCursor() => Console.Write("\x1b[?25h");
    public void SaveCursorPosition() => Console.Write("\x1b[s");
    public void RestoreCursorPosition() => Console.Write("\x1b[u");
    public void ClearScreen() => Console.Write("\x1b[2J\x1b[H");
    public void ClearLineFromCursor() => Console.Write("\x1b[K");
    public void Flush() => Console.Out.Flush();

    private void CheckResize(object? state)
    {
        var newWidth = Console.WindowWidth > 0 ? Console.WindowWidth : 80;
        var newHeight = Console.WindowHeight > 0 ? Console.WindowHeight : 24;

        if (newWidth != _cachedWidth || newHeight != _cachedHeight)
        {
            _cachedWidth = newWidth;
            _cachedHeight = newHeight;
            OnResized?.Invoke(new Size(newWidth, newHeight));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _resizeTimer.Dispose();
    }
}
