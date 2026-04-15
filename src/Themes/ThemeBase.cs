using System;
using System.Text;

namespace Radiance.Themes;

/// <summary>
/// Base class for themes providing ANSI color helpers and prompt segment utilities.
/// </summary>
public abstract class ThemeBase : ITheme
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract string Author { get; }
    public abstract string RenderPrompt(PromptContext ctx);
    public abstract string RenderRightPrompt(PromptContext ctx);

    // ─── ANSI Helpers ───────────────────────────────────────────────

    protected static string Reset => "\x1b[0m";
    protected static string Bold => "\x1b[1m";
    protected static string Dim => "\x1b[2m";
    protected static string Underline => "\x1b[4m";
    protected static string Italic => "\x1b[3m";

    protected static string Fg(AnsiColor color) => $"\x1b[{(int)color}m";
    protected static string Bg(AnsiColor color) => $"\x1b[{(int)color + 10}m";

    protected static string FgRgb(int r, int g, int b) => $"\x1b[38;2;{r};{g};{b}m";
    protected static string BgRgb(int r, int g, int b) => $"\x1b[48;2;{r};{g};{b}m";

    // ─── Segment Builder ────────────────────────────────────────────

    /// <summary>
    /// Colors a string with a foreground color and optional bold.
    /// </summary>
    protected static string Color(string text, AnsiColor fg, bool bold = false)
    {
        var sb = new StringBuilder();
        if (bold) sb.Append(Bold);
        sb.Append(Fg(fg));
        sb.Append(text);
        sb.Append(Reset);
        return sb.ToString();
    }

    /// <summary>
    /// Colors a string with foreground and background.
    /// </summary>
    protected static string Color(string text, AnsiColor fg, AnsiColor bg, bool bold = false)
    {
        var sb = new StringBuilder();
        if (bold) sb.Append(Bold);
        sb.Append(Fg(fg));
        sb.Append(Bg(bg));
        sb.Append(text);
        sb.Append(Reset);
        return sb.ToString();
    }

    /// <summary>
    /// Colors a string with RGB foreground.
    /// </summary>
    protected static string ColorRgb(string text, int r, int g, int b, bool bold = false)
    {
        var sb = new StringBuilder();
        if (bold) sb.Append(Bold);
        sb.Append(FgRgb(r, g, b));
        sb.Append(text);
        sb.Append(Reset);
        return sb.ToString();
    }

    // ─── Prompt Helpers ─────────────────────────────────────────────

    /// <summary>
    /// Formats the git branch segment if inside a git repo.
    /// </summary>
    protected static string FormatGit(PromptContext ctx, string? prefix = null, string? suffix = null)
    {
        if (string.IsNullOrEmpty(ctx.GitBranch)) return "";
        var branch = ctx.GitBranch;
        if (ctx.GitDirty) branch += "*";
        return $"{prefix}{branch}{suffix}";
    }

    /// <summary>
    /// Returns the prompt character, colored by exit code.
    /// </summary>
    protected static string FormatPromptChar(PromptContext ctx, string ch = "$",
        AnsiColor successColor = AnsiColor.Green, AnsiColor errorColor = AnsiColor.Red)
    {
        var color = ctx.LastExitCode == 0 ? successColor : errorColor;
        return Color(ch, color, bold: true);
    }

    /// <summary>
    /// Returns a right-aligned string for RPROMPT using ANSI escape sequences.
    /// </summary>
    protected static string AlignRight(string text)
    {
        // We use a mark-and-restore approach:
        // Save cursor, move to right edge, print text, restore cursor
        return $"\x1b[s\x1b[999C\x1b[{TextWidth(text)}D{text}\x1b[u";
    }

    /// <summary>
    /// Estimates the visible width of text (excluding ANSI escape sequences).
    /// </summary>
    public static int TextWidth(string text)
    {
        int width = 0;
        bool inEscape = false;
        foreach (char c in text)
        {
            if (c == '\x1b') { inEscape = true; continue; }
            if (inEscape)
            {
                if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z') inEscape = false;
                continue;
            }
            width++;
        }
        return width;
    }
}

/// <summary>
/// Standard ANSI 256-color palette values (foreground codes).
/// </summary>
public enum AnsiColor
{
    Black = 30,
    Red = 31,
    Green = 32,
    Yellow = 33,
    Blue = 34,
    Magenta = 35,
    Cyan = 36,
    White = 37,
    BrightBlack = 90,
    BrightRed = 91,
    BrightGreen = 92,
    BrightYellow = 93,
    BrightBlue = 94,
    BrightMagenta = 95,
    BrightCyan = 96,
    BrightWhite = 97,
    Default = 39,
}