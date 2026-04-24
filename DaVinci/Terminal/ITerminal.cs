namespace DaVinci.Terminal;

public interface ITerminal : IDisposable
{
    int Width { get; }
    int Height { get; }
    int CursorLeft { get; }
    int CursorTop { get; }
    bool IsOutputRedirected { get; }

    void Write(string text);
    void WriteLine();
    void SetCursorPosition(int left, int top);
    void HideCursor();
    void ShowCursor();
    void SaveCursorPosition();
    void RestoreCursorPosition();
    void ClearScreen();
    void ClearLineFromCursor();
    void Flush();

    event Action<Size>? OnResized;
}

public readonly struct Size(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
}
