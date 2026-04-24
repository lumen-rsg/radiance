using System.Text;

namespace DaVinci.Terminal;

public sealed class AnsiBuilder
{
    private readonly StringBuilder _sb = new();

    // Reset
    public AnsiBuilder Reset() { _sb.Append(AnsiCodes.Reset); return this; }

    // Text attributes
    public AnsiBuilder Bold() { _sb.Append(AnsiCodes.Bold); return this; }
    public AnsiBuilder Dim() { _sb.Append(AnsiCodes.Dim); return this; }
    public AnsiBuilder Italic() { _sb.Append(AnsiCodes.Italic); return this; }
    public AnsiBuilder Underline() { _sb.Append(AnsiCodes.Underline); return this; }
    public AnsiBuilder Blink() { _sb.Append(AnsiCodes.Blink); return this; }
    public AnsiBuilder Reverse() { _sb.Append(AnsiCodes.Reverse); return this; }
    public AnsiBuilder Strikethrough() { _sb.Append(AnsiCodes.Strikethrough); return this; }

    // Foreground colors
    public AnsiBuilder Fg(AnsiNamedColor c) { _sb.Append(AnsiCodes.Fg(c)); return this; }
    public AnsiBuilder FgRgb(int r, int g, int b) { _sb.Append(AnsiCodes.FgRgb(r, g, b)); return this; }
    public AnsiBuilder Fg256(int idx) { _sb.Append(AnsiCodes.Fg256(idx)); return this; }
    public AnsiBuilder Fg(Color color) { _sb.Append(color.ToFgAnsi()); return this; }
    public AnsiBuilder FgDefault() { _sb.Append(AnsiCodes.FgDefault); return this; }

    // Background colors
    public AnsiBuilder Bg(AnsiNamedColor c) { _sb.Append(AnsiCodes.Bg(c)); return this; }
    public AnsiBuilder BgRgb(int r, int g, int b) { _sb.Append(AnsiCodes.BgRgb(r, g, b)); return this; }
    public AnsiBuilder Bg256(int idx) { _sb.Append(AnsiCodes.Bg256(idx)); return this; }
    public AnsiBuilder Bg(Color color) { _sb.Append(color.ToBgAnsi()); return this; }
    public AnsiBuilder BgDefault() { _sb.Append(AnsiCodes.BgDefault); return this; }

    // Cursor movement
    public AnsiBuilder Move(int row, int col) { _sb.Append(AnsiCodes.Move(row, col)); return this; }
    public AnsiBuilder MoveUp(int n) { _sb.Append(AnsiCodes.MoveUp(n)); return this; }
    public AnsiBuilder MoveDown(int n) { _sb.Append(AnsiCodes.MoveDown(n)); return this; }
    public AnsiBuilder MoveRight(int n) { _sb.Append(AnsiCodes.MoveRight(n)); return this; }
    public AnsiBuilder MoveLeft(int n) { _sb.Append(AnsiCodes.MoveLeft(n)); return this; }
    public AnsiBuilder MoveToColumn(int col) { _sb.Append(AnsiCodes.MoveToColumn(col)); return this; }

    // Cursor save/restore
    public AnsiBuilder SaveCursor() { _sb.Append(AnsiCodes.SaveCursor); return this; }
    public AnsiBuilder RestoreCursor() { _sb.Append(AnsiCodes.RestoreCursor); return this; }

    // Cursor visibility
    public AnsiBuilder HideCursor() { _sb.Append(AnsiCodes.HideCursor); return this; }
    public AnsiBuilder ShowCursor() { _sb.Append(AnsiCodes.ShowCursor); return this; }

    // Clearing
    public AnsiBuilder ClearScreen() { _sb.Append(AnsiCodes.ClearScreen); return this; }
    public AnsiBuilder ClearLineForward() { _sb.Append(AnsiCodes.ClearLineForward); return this; }
    public AnsiBuilder ClearLineBackward() { _sb.Append(AnsiCodes.ClearLineBackward); return this; }
    public AnsiBuilder ClearLineFull() { _sb.Append(AnsiCodes.ClearLineFull); return this; }

    // Scrolling
    public AnsiBuilder ScrollUp(int n = 1) { _sb.Append(AnsiCodes.ScrollUp(n)); return this; }
    public AnsiBuilder ScrollDown(int n = 1) { _sb.Append(AnsiCodes.ScrollDown(n)); return this; }

    // Alt screen
    public AnsiBuilder EnterAltScreen() { _sb.Append(AnsiCodes.EnterAltScreen); return this; }
    public AnsiBuilder LeaveAltScreen() { _sb.Append(AnsiCodes.LeaveAltScreen); return this; }

    // Raw text
    public AnsiBuilder Text(string text) { _sb.Append(text); return this; }
    public AnsiBuilder Text(char c) { _sb.Append(c); return this; }
    public AnsiBuilder Line() { _sb.AppendLine(); return this; }

    // Styled text: opens style, writes text, closes style
    public AnsiBuilder Styled(string text, TextStyle style)
    {
        _sb.Append(style.ToAnsiOpen());
        _sb.Append(text);
        _sb.Append(style.ToAnsiClose());
        return this;
    }

    // Apply a full style (open sequence)
    public AnsiBuilder ApplyStyle(TextStyle style)
    {
        _sb.Append(style.ToAnsiOpen());
        return this;
    }

    // Reset style
    public AnsiBuilder ResetStyle()
    {
        _sb.Append(AnsiCodes.Reset);
        return this;
    }

    public override string ToString() => _sb.ToString();
    public void Clear() => _sb.Clear();
    public int Length => _sb.Length;
}
