using System.Text;
using DaVinci.Terminal;

namespace Radiance.Multiplexer;

/// <summary>
/// Headless ITerminal that captures Write() output to a buffer
/// instead of writing to the console. Used by MuxDaemon to capture
/// rendered frames for sending to connected clients.
/// </summary>
public sealed class HeadlessTerminal : ITerminal
{
    private readonly StringBuilder _output = new();
    private int _width;
    private int _height;

    public int Width => _width;
    public int Height => _height;
    public int CursorLeft => 0;
    public int CursorTop => 0;
    public bool IsOutputRedirected => true;

    public event Action<Size>? OnResized;

    public HeadlessTerminal(int width, int height)
    {
        _width = width;
        _height = height;
    }

    public void Write(string text) => _output.Append(text);
    public void WriteLine() => _output.AppendLine();
    public void Flush() { }

    public void SetCursorPosition(int left, int top) { }
    public void HideCursor() { }
    public void ShowCursor() { }
    public void SaveCursorPosition() { }
    public void RestoreCursorPosition() { }
    public void ClearScreen() { }
    public void ClearLineFromCursor() { }

    /// <summary>
    /// Drain all captured output and return it as a byte array.
    /// Clears the internal buffer.
    /// </summary>
    public byte[] DrainOutput()
    {
        if (_output.Length == 0) return Array.Empty<byte>();
        var bytes = Encoding.UTF8.GetBytes(_output.ToString());
        _output.Clear();
        return bytes;
    }

    /// <summary>
    /// Resize the virtual terminal dimensions.
    /// </summary>
    public void Resize(int newWidth, int newHeight)
    {
        if (newWidth == _width && newHeight == _height) return;
        _width = newWidth;
        _height = newHeight;
        OnResized?.Invoke(new Size(newWidth, newHeight));
    }

    public void Dispose() { }
}
