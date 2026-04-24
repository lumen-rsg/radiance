namespace DaVinci.Terminal;

public sealed class CursorManager
{
    private readonly ITerminal _terminal;
    private int _savedLeft;
    private int _savedTop;

    public int Left { get; private set; }
    public int Top { get; private set; }
    public bool IsHidden { get; private set; }

    public CursorManager(ITerminal terminal)
    {
        _terminal = terminal;
        Left = terminal.CursorLeft;
        Top = terminal.CursorTop;
    }

    public void MoveTo(int left, int top)
    {
        Left = Math.Max(0, left);
        Top = Math.Max(0, top);
        _terminal.SetCursorPosition(Left, Top);
    }

    public void MoveUp(int rows = 1)
    {
        Top = Math.Max(0, Top - rows);
        _terminal.SetCursorPosition(Left, Top);
    }

    public void MoveDown(int rows = 1)
    {
        Top += rows;
        _terminal.SetCursorPosition(Left, Top);
    }

    public void MoveLeft(int cols = 1)
    {
        Left = Math.Max(0, Left - cols);
        _terminal.SetCursorPosition(Left, Top);
    }

    public void MoveRight(int cols = 1)
    {
        Left += cols;
        _terminal.SetCursorPosition(Left, Top);
    }

    public void Save()
    {
        _savedLeft = Left;
        _savedTop = Top;
        _terminal.SaveCursorPosition();
    }

    public void Restore()
    {
        Left = _savedLeft;
        Top = _savedTop;
        _terminal.RestoreCursorPosition();
    }

    public void Hide()
    {
        if (!IsHidden)
        {
            IsHidden = true;
            _terminal.HideCursor();
        }
    }

    public void Show()
    {
        if (IsHidden)
        {
            IsHidden = false;
            _terminal.ShowCursor();
        }
    }
}
