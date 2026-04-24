namespace DaVinci.Terminal;

public static class AnsiCodes
{
    // Reset
    public const string Reset = "\x1b[0m";

    // Text attributes
    public const string Bold = "\x1b[1m";
    public const string Dim = "\x1b[2m";
    public const string Italic = "\x1b[3m";
    public const string Underline = "\x1b[4m";
    public const string Blink = "\x1b[5m";
    public const string Reverse = "\x1b[7m";
    public const string Strikethrough = "\x1b[9m";
    public const string BoldOff = "\x1b[22m";
    public const string DimOff = "\x1b[22m";
    public const string ItalicOff = "\x1b[23m";
    public const string UnderlineOff = "\x1b[24m";
    public const string BlinkOff = "\x1b[25m";
    public const string ReverseOff = "\x1b[27m";
    public const string StrikethroughOff = "\x1b[29m";

    // Foreground
    public static string Fg(AnsiNamedColor c) => $"\x1b[{(int)c}m";
    public static string FgRgb(int r, int g, int b) => $"\x1b[38;2;{r};{g};{b}m";
    public static string Fg256(int idx) => $"\x1b[38;5;{idx}m";
    public static string FgDefault => "\x1b[39m";

    // Background
    public static string Bg(AnsiNamedColor c) => $"\x1b[{(int)c + 10}m";
    public static string BgRgb(int r, int g, int b) => $"\x1b[48;2;{r};{g};{b}m";
    public static string Bg256(int idx) => $"\x1b[48;5;{idx}m";
    public static string BgDefault => "\x1b[49m";

    // Cursor movement
    public static string Move(int row, int col) => $"\x1b[{row};{col}H";
    public static string MoveUp(int n) => $"\x1b[{n}A";
    public static string MoveDown(int n) => $"\x1b[{n}B";
    public static string MoveRight(int n) => $"\x1b[{n}C";
    public static string MoveLeft(int n) => $"\x1b[{n}D";
    public static string MoveToColumn(int col) => $"\x1b[{col}G";

    // Cursor save/restore
    public const string SaveCursor = "\x1b[s";
    public const string RestoreCursor = "\x1b[u";

    // Cursor visibility
    public const string HideCursor = "\x1b[?25l";
    public const string ShowCursor = "\x1b[?25h";

    // Clearing
    public const string ClearScreen = "\x1b[2J\x1b[H";
    public const string ClearLineForward = "\x1b[K";
    public const string ClearLineBackward = "\x1b[1K";
    public const string ClearLineFull = "\x1b[2K";
    public static string ClearLinesDown(int n) => $"\x1b[{n}M";
    public static string InsertLines(int n) => $"\x1b[{n}L";

    // Scrolling
    public static string ScrollUp(int n) => $"\x1b[{n}S";
    public static string ScrollDown(int n) => $"\x1b[{n}T";

    // Alternative screen buffer
    public const string EnterAltScreen = "\x1b[?1049h";
    public const string LeaveAltScreen = "\x1b[?1049l";

    /// <summary>
    /// Computes the visible width of text, excluding ANSI escape sequences
    /// and accounting for wide characters (CJK, emoji).
    /// </summary>
    public static int VisibleWidth(ReadOnlySpan<char> text)
    {
        var width = 0;
        var i = 0;

        while (i < text.Length)
        {
            if (text[i] == '\x1b' && i + 1 < text.Length && text[i + 1] == '[')
            {
                // Skip ANSI escape sequence
                i += 2;
                while (i < text.Length)
                {
                    var c = text[i];
                    i++;
                    if (c is >= '@' and <= '~')
                        break;
                }
            }
            else if (char.IsControl(text[i]))
            {
                i++;
            }
            else
            {
                width += Cell.GetDisplayWidth(text[i]);
                i++;
            }
        }

        return width;
    }

    /// <summary>
    /// Overload for string input.
    /// </summary>
    public static int VisibleWidth(string text) => VisibleWidth(text.AsSpan());
}
