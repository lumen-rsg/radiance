using DaVinci.Components;
using DaVinci.Terminal;
using Radiance.Utils;

namespace Radiance.Terminal;

/// <summary>
/// Constructs DaVinci-based views for the Radiance shell.
/// Each method writes directly to a <see cref="TerminalBuffer"/>,
/// translating the original SparkleRenderer layouts into
/// DaVinci cell/styled-text operations.
/// </summary>
public static class DaVinciViewBuilder
{
    // ─── Logo data (from SparkleRenderer) ─────────────────────────────

    private static readonly string[] LogoLines =
    [
        "  ╔══════════════════════════════════╗",
        "  ║ ░█▀▄░█▀█░█▀▄░▀█▀░█▀█░█▀█░█▀▀░█▀▀ ║",
        "  ║ ░█▀▄░█▀█░█░█░░█░░█▀█░█░█░█░░░█▀▀ ║",
        "  ║ ░▀░▀░▀░▀░▀▀░░▀▀▀░▀░▀░▀░▀░▀▀▀░▀▀▀ ║",
        "  ╚══════════════════════════════════╝",
    ];

    /// <summary>
    /// DaVinci TextStyle gradient for each logo line.
    /// Mapped from the original raw-ANSI LogoGradient in SparkleRenderer.
    /// </summary>
    private static readonly TextStyle[] LogoGradientStyles =
    [
        new() { Foreground = Color.BrightCyan, Bold = true },
        new() { Foreground = Color.BrightCyan, Bold = true },
        new() { Foreground = Color.FromIndex(51) },
        new() { Foreground = Color.FromIndex(87) },
        new() { Foreground = Color.FromIndex(213) },
        new() { Foreground = Color.FromIndex(219) },
        new() { Foreground = Color.FromIndex(213) },
        new() { Foreground = Color.FromIndex(87) },
        new() { Foreground = Color.FromIndex(51) },
        new() { Foreground = Color.BrightCyan, Bold = true },
    ];

    private static readonly string[] FortuneMessages =
    [
        "There are only 10 types of people: those who understand binary and those who don't.",
        "A SQL query walks into a bar, sees two tables, and asks: \"Can I JOIN you?\"",
        "There's no place like 127.0.0.1",
        "It works on my machine. ¯\\_(ツ)_/¯",
        "In a world without fences and walls, who needs Gates and Windows?",
        "The best thing about a boolean is even if you are wrong, you are only off by a bit.",
        "There are 2 hard problems in computer science: cache invalidation, naming things, and off-by-1 errors.",
        "A programmer's wife tells him: \"Go to the store and buy a loaf of bread. If they have eggs, buy a dozen.\" He comes home with 12 loaves.",
        "Why do programmers prefer dark mode? Because light attracts bugs.",
        "\"To iterate is human, to recurse divine.\" — L. Peter Deutsch",
        "The three great virtues of a programmer: laziness, impatience, and hubris. — Larry Wall",
        "First, solve the problem. Then, write the code. — John Johnson",
        "Talk is cheap. Show me the code. — Linus Torvalds",
        "Any sufficiently advanced technology is indistinguishable from magic. — Arthur C. Clarke",
        "In case of fire: git commit, git push, leave building.",
        "The cloud is just someone else's computer.",
        "Unfortunate fortune: Segmentation fault (core dumped)",
        "alias leave='sudo shutdown -h now'",
        "echo 'Have you tried turning it off and on again?'",
        "Perfection is achieved not when there is nothing more to add, but when there is nothing left to take away. — Antoine de Saint-Exupéry",
        "Simplicity is prerequisite for reliability. — Edsger W. Dijkstra",
        "Code is like humor. When you have to explain it, it's bad. — Cory House",
        "Fix the cause, not the symptom. — Steve Maguire",
        "Optimism is an occupational hazard of programming: feedback is the treatment. — Kent Beck",
    ];

    private static readonly string[] SparkleChars = ["✦", "✧", "⋆", "✶", "✴", "✵", "★", "☆", "·", "•"];

    private static readonly Color[] SparkleColors =
    [
        Color.BrightYellow,
        Color.BrightCyan,
        Color.BrightMagenta,
        Color.FromIndex(213),
        Color.FromIndex(228),
        Color.FromIndex(51),
        Color.BrightWhite,
    ];

    private static readonly Random Rng = Random.Shared;

    // ─── Static Views ─────────────────────────────────────────────────

    /// <summary>
    /// Renders the Radiance welcome banner with gradient-colored logo,
    /// version line, and help text.
    /// </summary>
    public static void RenderWelcomeBanner(TerminalBuffer buffer, int w, int h, string version)
    {
        var y = Math.Max(0, (h - LogoLines.Length - 4) / 2);

        foreach (var line in LogoLines)
        {
            var lineVisibleWidth = AnsiCodes.VisibleWidth(line);
            var x = Math.Max(0, (w - lineVisibleWidth) / 2);
            var styleIndex = y - Math.Max(0, (h - LogoLines.Length - 4) / 2);
            var style = styleIndex < LogoGradientStyles.Length
                ? LogoGradientStyles[styleIndex]
                : LogoGradientStyles[0];
            buffer.SetText(x, y, line, style);
            y++;
        }

        y++;

        // Version line
        var versionText = $"✦ Radiance Shell v{version} — A BASH interpreter in C# ✦";
        var versionStyle = new TextStyle { Foreground = Color.BrightYellow, Bold = true };
        var vx = Math.Max(0, (w - AnsiCodes.VisibleWidth(versionText)) / 2);
        buffer.SetText(vx, y, versionText, versionStyle);
        y++;

        // Help line
        var helpText = "Type 'exit' to quit. Type 'radiance help' for fun commands.";
        var helpStyle = new TextStyle { Dim = true };
        var hx = Math.Max(0, (w - AnsiCodes.VisibleWidth(helpText)) / 2);
        buffer.SetText(hx, y, helpText, helpStyle);
    }

    /// <summary>
    /// Renders the session statistics dashboard with rounded box borders,
    /// session info, and top-command bar charts.
    ///
    /// Box layout (contentWidth = innerWidth):
    ///   ╭──────────────────────────────────────────────╮
    ///   │ content padded to innerWidth                 │
    ///   ╰──────────────────────────────────────────────╯
    ///   x                                              x + innerWidth + 1
    /// </summary>
    public static void RenderStatsDashboard(TerminalBuffer buffer, int w, int h, SessionStats stats)
    {
        const int contentWidth = 46; // width of content between │ borders
        var totalWidth = contentWidth + 2; // including both border chars
        var totalLines = 6 + (stats.TopCommands.Count > 0 ? 2 + stats.TopCommands.Count : 0) + 1;
        var startX = Math.Max(0, (w - totalWidth) / 2);
        var startY = Math.Max(0, (h - totalLines) / 2);

        var borderStyle = new TextStyle { Foreground = Color.BrightCyan };
        var labelStyle = new TextStyle { Foreground = Color.BrightYellow, Bold = true };
        var barStyle = new TextStyle { Foreground = Color.BrightYellow, Bold = true };
        var barLabelStyle = new TextStyle { Foreground = Color.FromIndex(213) };
        var headerStyle = new TextStyle { Foreground = Color.BrightWhite };
        var chars = BoxDrawingChars.GetChars(BoxBorderStyle.Rounded);

        var x = startX;
        var y = startY;

        // Top border: ╭ + contentWidth dashes + ╮
        DrawHorizontalBorder(buffer, x, y, contentWidth, chars.TopLeft, chars.TopRight, chars.Horizontal, borderStyle);
        y++;

        var uptime = DateTime.Now - stats.SessionStart;

        DrawContentLine(buffer, x, y, contentWidth, chars.Vertical, borderStyle,
            new List<(string text, TextStyle style)>
            {
                ("  ", TextStyle.Empty),
                ("✦ Session Started:  ", labelStyle),
                ($"{stats.SessionStart:yyyy-MM-dd HH:mm:ss}", TextStyle.Empty),
            });
        y++;

        DrawContentLine(buffer, x, y, contentWidth, chars.Vertical, borderStyle,
            new List<(string text, TextStyle style)>
            {
                ("  ", TextStyle.Empty),
                ("✦ Uptime:            ", labelStyle),
                (FormatDuration(uptime), TextStyle.Empty),
            });
        y++;

        DrawContentLine(buffer, x, y, contentWidth, chars.Vertical, borderStyle,
            new List<(string text, TextStyle style)>
            {
                ("  ", TextStyle.Empty),
                ("✦ Commands Run:       ", labelStyle),
                ($"{stats.CommandCount}", TextStyle.Empty),
            });
        y++;

        DrawContentLine(buffer, x, y, contentWidth, chars.Vertical, borderStyle,
            new List<(string text, TextStyle style)>
            {
                ("  ", TextStyle.Empty),
                ("✦ Unique Commands:    ", labelStyle),
                ($"{stats.UniqueCommands}", TextStyle.Empty),
            });
        y++;

        if (stats.TopCommands.Count > 0)
        {
            DrawContentLine(buffer, x, y, contentWidth, chars.Vertical, borderStyle,
                "");
            y++;

            DrawContentLine(buffer, x, y, contentWidth, chars.Vertical, borderStyle,
                new List<(string text, TextStyle style)>
                {
                    ("  ", TextStyle.Empty),
                    ("── Top Commands ──", headerStyle),
                });
            y++;

            foreach (var (cmd, count) in stats.TopCommands)
            {
                var bar = new string('█', Math.Min(count, 20));
                var cmdLabel = cmd.Length > 10 ? cmd[..10] + "…" : cmd;

                DrawContentLine(buffer, x, y, contentWidth, chars.Vertical, borderStyle,
                    new List<(string text, TextStyle style)>
                    {
                        ("  ", TextStyle.Empty),
                        ($"{cmdLabel,-11}", barLabelStyle),
                        (" ", TextStyle.Empty),
                        (bar, barStyle),
                        (" ", TextStyle.Empty),
                        ($"{count,3}", TextStyle.Empty),
                    });
                y++;
            }
        }

        // Empty line before footer
        DrawContentLine(buffer, x, y, contentWidth, chars.Vertical, borderStyle, "");
        y++;

        // Bottom border: ╰ + contentWidth dashes + ╯
        DrawHorizontalBorder(buffer, x, y, contentWidth, chars.BottomLeft, chars.BottomRight, chars.Horizontal, borderStyle);
    }

    /// <summary>
    /// Renders a fortune cookie message inside a double-border box.
    /// </summary>
    public static void RenderFortune(TerminalBuffer buffer, int w, int h)
    {
        var fortune = FortuneMessages[Rng.Next(FortuneMessages.Length)];
        var fortuneColor = SparkleColors[Rng.Next(SparkleColors.Length)];
        var fortuneStyle = new TextStyle { Foreground = fortuneColor };

        var fullText = $"\ud83c\udf6a {fortune}";
        var textDisplayWidth = AnsiCodes.VisibleWidth(fullText);
        var contentWidth = Math.Min(textDisplayWidth + 4, w > 4 ? w - 4 : 76);
        var totalWidth = contentWidth + 2;

        var wrapped = WordWrap(fullText, contentWidth);
        var totalLines = wrapped.Count + 4;
        var startX = Math.Max(0, (w - totalWidth) / 2);
        var startY = Math.Max(0, (h - totalLines) / 2);

        var borderStyle = new TextStyle { Foreground = Color.BrightYellow, Bold = true };
        var chars = BoxDrawingChars.GetChars(BoxBorderStyle.Double);

        var x = startX;
        var y = startY;

        // Top border
        DrawHorizontalBorder(buffer, x, y, contentWidth, chars.TopLeft, chars.TopRight, chars.Horizontal, borderStyle);
        y++;

        // Empty line
        DrawContentLine(buffer, x, y, contentWidth, chars.Vertical, borderStyle, "");
        y++;

        // Fortune lines
        foreach (var line in wrapped)
        {
            DrawContentLine(buffer, x, y, contentWidth, chars.Vertical, borderStyle, line, fortuneStyle);
            y++;
        }

        // Empty line
        DrawContentLine(buffer, x, y, contentWidth, chars.Vertical, borderStyle, "");
        y++;

        // Bottom border
        DrawHorizontalBorder(buffer, x, y, contentWidth, chars.BottomLeft, chars.BottomRight, chars.Horizontal, borderStyle);
    }

    /// <summary>
    /// Renders the radiance help screen inside a rounded box.
    /// </summary>
    public static void RenderHelpScreen(TerminalBuffer buffer, int w, int h)
    {
        const int contentWidth = 28;
        var totalWidth = contentWidth + 2;
        var lines = new List<(string text, TextStyle? style)>
        {
            ("", null),
            ("  Usage:", new TextStyle { Foreground = Color.BrightYellow, Bold = true }),
            ("    radiance [command]", null),
            ("", null),
            ("  Commands:", new TextStyle { Foreground = Color.BrightYellow, Bold = true }),
            ("    (none)   Show logo", null),
            ("    spark    Sparkle!", null),
            ("    fortune  Fortune", null),
            ("    stats    Session info", null),
            ("    matrix   Enter Matrix", null),
            ("    help     This message", null),
            ("", null),
        };

        var totalLines = lines.Count + 2;
        var startX = Math.Max(0, (w - totalWidth) / 2);
        var startY = Math.Max(0, (h - totalLines) / 2);

        var borderStyle = new TextStyle { Foreground = Color.BrightCyan };
        var titleStyle = new TextStyle { Foreground = Color.BrightYellow, Bold = true };
        var chars = BoxDrawingChars.GetChars(BoxBorderStyle.Rounded);

        var x = startX;
        var y = startY;

        // Top border with centered title: ╭───────  radiance  ───────╮
        // Title = "  radiance  " (12 chars), rest is dashes to fill contentWidth
        var title = "  radiance  ";
        var titleVisibleWidth = AnsiCodes.VisibleWidth(title);
        var remainingDash = contentWidth - titleVisibleWidth;
        var leftDashCount = remainingDash / 2;
        var rightDashCount = remainingDash - leftDashCount;

        buffer.SetText(x, y, chars.TopLeft, borderStyle);
        buffer.SetText(x + 1, y, new string(chars.Horizontal[0], leftDashCount), borderStyle);
        buffer.SetText(x + 1 + leftDashCount, y, title, titleStyle);
        buffer.SetText(x + 1 + leftDashCount + titleVisibleWidth, y, new string(chars.Horizontal[0], rightDashCount), borderStyle);
        buffer.SetText(x + totalWidth - 1, y, chars.TopRight, borderStyle);
        y++;

        // Content lines
        foreach (var (text, style) in lines)
        {
            DrawContentLine(buffer, x, y, contentWidth, chars.Vertical, borderStyle, text, style);
            y++;
        }

        // Bottom border
        DrawHorizontalBorder(buffer, x, y, contentWidth, chars.BottomLeft, chars.BottomRight, chars.Horizontal, borderStyle);
    }

    // ─── Animations ───────────────────────────────────────────────────

    /// <summary>
    /// Sparkle cascade animation — random sparkle characters appearing
    /// and fading across the terminal.
    /// </summary>
    public static void RenderSparkleAnimation(TerminalBuffer buffer, int w, int h, int frame)
    {
        var sparkles = _sparkles;

        // Add new particles (3 per frame)
        for (var i = 0; i < 3; i++)
        {
            sparkles.Add((
                Rng.Next(w),
                Rng.Next(h),
                SparkleChars[Rng.Next(SparkleChars.Length)],
                SparkleColors[Rng.Next(SparkleColors.Length)],
                Rng.Next(3, 8)));
        }

        // Draw active sparkles
        foreach (var (col, row, ch, color, _) in sparkles)
        {
            if (row >= 0 && row < h && col >= 0 && col < w)
            {
                var style = new TextStyle { Foreground = color, Bold = true };
                buffer.SetText(col, row, ch, style);
            }
        }

        // Age sparkles and remove dead ones
        _sparkles = sparkles
            .Select(s => (s.col, s.row, s.ch, s.color, life: s.life - 1))
            .Where(s => s.life > 0)
            .ToList();
    }

    private static List<(int col, int row, string ch, Color color, int life)> _sparkles = [];

    /// <summary>
    /// Clears sparkle state. Called before starting a new animation.
    /// </summary>
    public static void ResetSparkles() => _sparkles = [];

    /// <summary>
    /// Matrix-style digital rain animation with green characters
    /// cascading down the terminal.
    /// </summary>
    public static void RenderMatrixAnimation(TerminalBuffer buffer, int w, int h, int frame)
    {
        var matrixChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789@#$%^&*(){}[]|;:<>?/~`";

        var (columns, speeds, trails) = GetOrCreateMatrixState(w);

        for (var col = 0; col < w; col++)
        {
            // Randomly activate columns
            if (columns[col] < 0 && Rng.Next(100) < 8)
                columns[col] = 0;

            if (columns[col] >= 0)
            {
                var row = columns[col];

                // Draw the head (bright white)
                if (row >= 0 && row < h)
                {
                    var ch = matrixChars[Rng.Next(matrixChars.Length)];
                    buffer.SetText(col, row, ch.ToString(), new TextStyle { Foreground = Color.BrightWhite, Bold = true });
                }

                // Draw the green trail
                for (var t = 1; t <= trails[col]; t++)
                {
                    var trailRow = row - t;
                    if (trailRow >= 0 && trailRow < h)
                    {
                        var tch = matrixChars[Rng.Next(matrixChars.Length)];
                        var dimFactor = t * 2;
                        if (dimFactor < 10)
                        {
                            buffer.SetText(col, trailRow, tch.ToString(),
                                new TextStyle { Foreground = Color.FromIndex((byte)(83 - dimFactor)) });
                        }
                    }
                }

                columns[col] += speeds[col];

                // Deactivate if fully off screen
                if (columns[col] - trails[col] > h)
                    columns[col] = -1;
            }
        }
    }

    private static (int[] columns, int[] speeds, int[] trails) _matrixState;

    private static (int[] columns, int[] speeds, int[] trails) GetOrCreateMatrixState(int width)
    {
        if (_matrixState.columns is null || _matrixState.columns.Length != width)
        {
            var columns = new int[width];
            var speeds = new int[width];
            var trails = new int[width];

            for (var i = 0; i < width; i++)
            {
                columns[i] = -1;
                speeds[i] = Rng.Next(1, 3);
                trails[i] = Rng.Next(3, 12);
            }

            _matrixState = (columns, speeds, trails);
        }

        return _matrixState;
    }

    /// <summary>Resets matrix state. Called before starting a new animation.</summary>
    public static void ResetMatrix() => _matrixState = default;

    // ─── Box Drawing Helpers ──────────────────────────────────────────
    //
    // All helpers use consistent dimension math:
    //   contentWidth = number of character cells between the two border chars
    //   totalWidth   = contentWidth + 2 (border char on each side)
    //   left border  at x
    //   right border at x + contentWidth + 1
    //   content fills from x+1 to x+contentWidth (padded with spaces)
    //

    /// <summary>
    /// Draws a horizontal border line: corner + contentWidth chars + corner.
    /// </summary>
    private static void DrawHorizontalBorder(
        TerminalBuffer buffer, int x, int y, int contentWidth,
        string leftCorner, string rightCorner, string fill, TextStyle style)
    {
        buffer.SetText(x, y, leftCorner, style);
        for (var i = 0; i < contentWidth; i++)
            buffer.SetText(x + 1 + i, y, fill, style);
        buffer.SetText(x + contentWidth + 1, y, rightCorner, style);
    }

    /// <summary>
    /// Draws a content line with uniform style: │ + content padded to contentWidth + │
    /// </summary>
    private static void DrawContentLine(
        TerminalBuffer buffer, int x, int y, int contentWidth,
        string vertical, TextStyle borderStyle,
        string content, TextStyle? contentStyle = null)
    {
        buffer.SetText(x, y, vertical, borderStyle);

        if (!string.IsNullOrEmpty(content))
        {
            buffer.SetText(x + 1, y, content, contentStyle ?? TextStyle.Empty);
            // Pad remaining with spaces
            var visibleLen = AnsiCodes.VisibleWidth(content);
            for (var i = visibleLen; i < contentWidth; i++)
                buffer.SetText(x + 1 + i, y, " ", TextStyle.Empty);
        }
        else
        {
            for (var i = 0; i < contentWidth; i++)
                buffer.SetText(x + 1 + i, y, " ", TextStyle.Empty);
        }

        buffer.SetText(x + contentWidth + 1, y, vertical, borderStyle);
    }

    /// <summary>
    /// Draws a content line with multiple styled segments: │ + segments padded to contentWidth + │
    /// </summary>
    private static void DrawContentLine(
        TerminalBuffer buffer, int x, int y, int contentWidth,
        string vertical, TextStyle borderStyle,
        List<(string text, TextStyle style)> segments)
    {
        buffer.SetText(x, y, vertical, borderStyle);

        var col = x + 1;
        foreach (var (text, style) in segments)
        {
            if (!string.IsNullOrEmpty(text))
            {
                buffer.SetText(col, y, text, style);
                col += AnsiCodes.VisibleWidth(text);
            }
        }

        // Pad remaining with spaces up to contentWidth
        var contentEnd = x + 1 + contentWidth;
        for (; col < contentEnd; col++)
            buffer.SetText(col, y, " ", TextStyle.Empty);

        buffer.SetText(x + contentWidth + 1, y, vertical, borderStyle);
    }

    // ─── Utility Helpers ───────────────────────────────────────────────

    private static string FormatDuration(TimeSpan span)
    {
        var parts = new List<string>();
        if (span.Hours > 0)
            parts.Add($"{span.Hours}h");
        if (span.Minutes > 0)
            parts.Add($"{span.Minutes}m");
        parts.Add($"{span.Seconds}s");
        return string.Join(" ", parts);
    }

    private static List<string> WordWrap(string text, int maxWidth)
    {
        var lines = new List<string>();
        if (maxWidth <= 0) { lines.Add(text); return lines; }

        var words = text.Split(' ');
        var current = new System.Text.StringBuilder();

        foreach (var word in words)
        {
            if (current.Length + word.Length + 1 > maxWidth)
            {
                if (current.Length > 0)
                {
                    lines.Add(current.ToString().TrimEnd());
                    current.Clear();
                }

                if (word.Length > maxWidth)
                {
                    lines.Add(word[..maxWidth]);
                    continue;
                }
            }

            if (current.Length > 0)
                current.Append(' ');
            current.Append(word);
        }

        if (current.Length > 0)
            lines.Add(current.ToString().TrimEnd());

        return lines.Count > 0 ? lines : [text];
    }
}
