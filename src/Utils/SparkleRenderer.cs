using System.Text;

namespace Radiance.Utils;

/// <summary>
/// Renders ANSI art, sparkle effects, matrix rain, and fortune messages
/// for the Radiance shell's signature visual flair.
/// </summary>
public static class SparkleRenderer
{
    /// <summary>
    /// The Radiance ASCII art logo with gradient ANSI coloring.
    /// </summary>
    private static readonly string[] LogoLines =
    [
        "  ╔══════════════════════════════════╗",
        "  ║ ░█▀▄░█▀█░█▀▄░▀█▀░█▀█░█▀█░█▀▀░█▀▀ ║",
        "  ║ ░█▀▄░█▀█░█░█░░█░░█▀█░█░█░█░░░█▀▀ ║",
        "  ║ ░▀░▀░▀░▀░▀▀░░▀▀▀░▀░▀░▀░▀░▀▀▀░▀▀▀ ║",
        "  ╚══════════════════════════════════╝",
    ];
    /// <summary>
    /// ANSI color gradient for the logo lines (index into the gradient array).
    /// </summary>
    private static readonly string[] LogoGradient =
    [
        "\x1b[1;36m", // bright cyan
        "\x1b[1;36m", // bright cyan
        "\x1b[38;5;51m", // bright aqua
        "\x1b[38;5;87m", // spring green
        "\x1b[38;5;213m", // pink
        "\x1b[38;5;219m", // light pink
        "\x1b[38;5;213m", // pink
        "\x1b[38;5;87m", // spring green
        "\x1b[38;5;51m", // bright aqua
        "\x1b[1;36m", // bright cyan
        "\x1b[1;33m", // bright yellow
        "\x1b[1;36m", // bright cyan
    ];

    /// <summary>
    /// Fortune messages — nerdy, developer-themed, and shell-themed quotes.
    /// </summary>
    private static readonly string[] Fortunes =
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

    private static readonly Random Rng = Random.Shared;

    /// <summary>
    /// Sparkle characters used for visual effects.
    /// </summary>
    private static readonly string[] SparkleChars = ["✦", "✧", "⋆", "✶", "✴", "✵", "★", "☆", "·", "•"];

    /// <summary>
    /// ANSI color palette for sparkle effects.
    /// </summary>
    private static readonly string[] SparkleColors =
    [
        "\x1b[1;33m", // bright yellow
        "\x1b[1;36m", // bright cyan
        "\x1b[1;35m", // bright magenta
        "\x1b[38;5;213m", // pink
        "\x1b[38;5;228m", // light gold
        "\x1b[38;5;51m", // aqua
        "\x1b[1;37m", // bright white
    ];

    /// <summary>
    /// Renders the Radiance ASCII art logo with gradient coloring.
    /// </summary>
    public static void RenderLogo(string version)
    {
        for (var i = 0; i < LogoLines.Length; i++)
        {
            var color = i < LogoGradient.Length ? LogoGradient[i] : "\x1b[1;36m";
            Console.WriteLine($"{color}{LogoLines[i]}\x1b[0m");
        }

        Console.WriteLine();
        Console.WriteLine($"\x1b[1;33m  ✦ Radiance Shell v{version} — A BASH interpreter in C# ✦\x1b[0m");
        Console.WriteLine("\x1b[37m  Type 'exit' to quit. Type 'help radiance' for fun commands.\x1b[0m");
        Console.WriteLine();
    }

    /// <summary>
    /// Renders a sparkle cascade animation — random sparkles appearing
    /// across the terminal with fading effects.
    /// </summary>
    /// <param name="durationMs">Duration of the animation in milliseconds (default 1500ms).</param>
    public static void RenderSparkle(int durationMs = 1500)
    {
        var width = Console.WindowWidth > 0 ? Console.WindowWidth : 80;
        var height = Console.WindowHeight > 0 ? Console.WindowHeight : 24;
        var startTime = Environment.TickCount64;

        // Save cursor position
        Console.Write("\x1b[s");

        // Hide cursor during animation
        Console.Write("\x1b[?25l");

        var sparkles = new List<(int col, int row, string ch, string color, int life)>();

        try
        {
            while (Environment.TickCount64 - startTime < durationMs)
            {
                // Add new sparkles
                for (var i = 0; i < 3; i++)
                {
                    var col = Rng.Next(width);
                    var row = Rng.Next(height);
                    var ch = SparkleChars[Rng.Next(SparkleChars.Length)];
                    var color = SparkleColors[Rng.Next(SparkleColors.Length)];
                    sparkles.Add((col, row, ch, color, Rng.Next(3, 8)));
                }

                // Draw active sparkles
                foreach (var (col, row, ch, color, life) in sparkles)
                {
                    if (row >= 0 && row < height && col >= 0 && col < width)
                    {
                        Console.Write($"\x1b[{row + 1};{col + 1}H{color}{ch}\x1b[0m");
                    }
                }

                // Age sparkles and remove dead ones
                sparkles = sparkles
                    .Select(s => (s.col, s.row, s.ch, s.color, life: s.life - 1))
                    .Where(s => s.life > 0)
                    .ToList();

                Thread.Sleep(50);
            }

            // Clear the sparkles by refreshing the screen area
            foreach (var (col, row, _, _, _) in sparkles)
            {
                if (row >= 0 && row < height && col >= 0 && col < width)
                {
                    Console.Write($"\x1b[{row + 1};{col + 1}H ");
                }
            }
        }
        finally
        {
            // Restore cursor and show it
            Console.Write("\x1b[u");
            Console.Write("\x1b[?25h");
        }

        // Print a final sparkle message
        var msgColor = SparkleColors[Rng.Next(SparkleColors.Length)];
        Console.WriteLine($"{msgColor}✦ Sparkle mode complete! ✦\x1b[0m");
    }

    /// <summary>
    /// Renders a Matrix-style digital rain animation with green characters
    /// cascading down the terminal.
    /// </summary>
    /// <param name="durationMs">Duration of the animation in milliseconds (default 2000ms).</param>
    public static void RenderMatrix(int durationMs = 2000)
    {
        var width = Console.WindowWidth > 0 ? Console.WindowWidth : 80;
        var height = Console.WindowHeight > 0 ? Console.WindowHeight : 24;

        // Characters to use in the rain
        var chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789@#$%^&*(){}[]|;:<>?/~`";

        // Track the "head" position and trail length for each column
        var columns = new int[width];       // current head row for each column
        var speeds = new int[width];        // speed (rows per tick) for each column
        var trails = new int[width];        // trail length for each column

        for (var i = 0; i < width; i++)
        {
            columns[i] = -1; // not active yet
            speeds[i] = Rng.Next(1, 3);
            trails[i] = Rng.Next(3, Math.Max(4, height / 3));
        }

        var startTime = Environment.TickCount64;

        // Hide cursor during animation
        Console.Write("\x1b[?25l");
        Console.Write("\x1b[s");

        try
        {
            while (Environment.TickCount64 - startTime < durationMs)
            {
                for (var col = 0; col < width; col++)
                {
                    // Randomly activate columns
                    if (columns[col] < 0 && Rng.Next(100) < 8)
                    {
                        columns[col] = 0;
                    }

                    if (columns[col] >= 0)
                    {
                        var row = columns[col];

                        // Draw the head (bright white-green)
                        if (row >= 0 && row < height)
                        {
                            var ch = chars[Rng.Next(chars.Length)];
                            Console.Write($"\x1b[{row + 1};{col + 1}H\x1b[1;97m{ch}\x1b[0m");
                        }

                        // Draw the bright green trail
                        for (var t = 1; t <= trails[col]; t++)
                        {
                            var trailRow = row - t;
                            if (trailRow >= 0 && trailRow < height)
                            {
                                var tch = chars[Rng.Next(chars.Length)];
                                var dimFactor = t * 2;
                                if (dimFactor < 10)
                                {
                                    Console.Write($"\x1b[{trailRow + 1};{col + 1}H\x1b[38;5;{83 - dimFactor}m{tch}\x1b[0m");
                                }
                                else
                                {
                                    // Clear old trail characters
                                    Console.Write($"\x1b[{trailRow + 1};{col + 1}H ");
                                }
                            }
                        }

                        // Clear the end of the trail
                        var clearRow = row - trails[col] - 1;
                        if (clearRow >= 0 && clearRow < height)
                        {
                            Console.Write($"\x1b[{clearRow + 1};{col + 1}H ");
                        }

                        columns[col] += speeds[col];

                        // Deactivate if off screen
                        if (columns[col] - trails[col] > height)
                        {
                            columns[col] = -1;
                        }
                    }
                }

                Thread.Sleep(40);
            }
        }
        finally
        {
            // Clear the screen and restore cursor
            Console.Write("\x1b[2J\x1b[H");
            Console.Write("\x1b[?25h");
            Console.Write("\x1b[u");
        }

        Console.WriteLine("\x1b[1;32mWake up, Radiance... The Matrix has you.\x1b[0m");
        Console.WriteLine();
    }

    /// <summary>
    /// Displays a random fortune cookie message with decorative styling.
    /// </summary>
    public static void RenderFortune()
    {
        var fortune = Fortunes[Rng.Next(Fortunes.Length)];
        var fortuneColor = SparkleColors[Rng.Next(SparkleColors.Length)];

        // Calculate display width of the full text (🍪 is 2 display columns)
        var fullText = $"🍪 {fortune}";
        var textDisplayWidth = VisibleLength(fullText);
        var maxWidth = Math.Min(textDisplayWidth + 4, Console.WindowWidth > 4 ? Console.WindowWidth - 4 : 76);
        var innerWidth = maxWidth - 2;

        var topBottom = new string('═', maxWidth);
        var emptyLine = new string(' ', maxWidth);

        Console.WriteLine($"\x1b[1;33m  ╔{topBottom}╗\x1b[0m");
        Console.WriteLine($"\x1b[1;33m  ║{emptyLine}║\x1b[0m");

        // Word-wrap the fortune using display width
        var lines = WordWrap(fullText, innerWidth);

        foreach (var line in lines)
        {
            var pad = Math.Max(0, innerWidth - VisibleLength(line));
            var padded = line + new string(' ', pad);
            Console.WriteLine($"\x1b[1;33m  ║ \x1b[0m{fortuneColor}{padded}\x1b[1;33m ║\x1b[0m");
        }

        Console.WriteLine($"\x1b[1;33m  ║{emptyLine}║\x1b[0m");
        Console.WriteLine($"\x1b[1;33m  ╚{topBottom}╝\x1b[0m");
        Console.WriteLine();
    }

    /// <summary>
    /// Renders a stylish session stats dashboard with properly aligned borders.
    /// </summary>
    /// <param name="stats">The session statistics to display.</param>
    public static void RenderStats(SessionStats stats)
    {
        const int W = 44; // Inner width between │ borders
        var uptime = DateTime.Now - stats.SessionStart;
        var hLine = new string('─', W);

        // Header
        Console.WriteLine($"\x1b[1;36m  ╭{hLine}╮\x1b[0m");
        BoxLine(W, "");

        // Session info
        BoxLine(W, $"  \x1b[1;33m✦ Session Started:\x1b[0m  {stats.SessionStart:yyyy-MM-dd HH:mm:ss}");
        BoxLine(W, $"  \x1b[1;33m✦ Uptime:\x1b[0m            {FormatDuration(uptime)}");
        BoxLine(W, $"  \x1b[1;33m✦ Commands Run:\x1b[0m       {stats.CommandCount}");
        BoxLine(W, $"  \x1b[1;33m✦ Unique Commands:\x1b[0m    {stats.UniqueCommands}");

        if (stats.TopCommands.Count > 0)
        {
            BoxLine(W, "");
            BoxLine(W, $"  \x1b[1;37m── Top Commands ──\x1b[0m");
            foreach (var (cmd, count) in stats.TopCommands)
            {
                var bar = new string('█', Math.Min(count, 20));
                var label = cmd.Length > 10 ? cmd[..10] + "…" : cmd;
                BoxLine(W, $"  \x1b[38;5;213m{label,-11}\x1b[0m \x1b[1;33m{bar}\x1b[0m {count,3}");
            }
        }

        BoxLine(W, "");

        // Footer
        Console.WriteLine($"\x1b[1;36m  ╰{hLine}╯\x1b[0m");
        Console.WriteLine();
    }

    /// <summary>
    /// Writes a single line inside a box border, right-padding to the given width.
    /// ANSI escape codes in <paramref name="content"/> are not counted as visible characters.
    /// Uses the default border color (cyan).
    /// </summary>
    internal static void BoxLine(int width, string content)
    {
        BoxLine(width, content, "\x1b[1;36m");
    }

    /// <summary>
    /// Writes a single line inside a box border with a specified border color,
    /// right-padding to the given width.
    /// ANSI escape codes in <paramref name="content"/> are not counted as visible characters.
    /// </summary>
    internal static void BoxLine(int width, string content, string borderColor)
    {
        var pad = Math.Max(0, width - VisibleLength(content));
        Console.WriteLine($"{borderColor}  │\x1b[0m{content}{new string(' ', pad)}{borderColor}│\x1b[0m");
    }

    /// <summary>
    /// Computes the visible (display-column) length of a string,
    /// ignoring ANSI escape sequences and counting surrogate pairs as 2 columns.
    /// </summary>
    internal static int VisibleLength(string text)
    {
        var len = 0;
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '\x1b')
            {
                // Skip ANSI escape sequence (\x1b[ ... m)
                i++;
                if (i < text.Length && text[i] == '[')
                {
                    i++;
                    while (i < text.Length && text[i] != 'm')
                        i++;
                    if (i < text.Length) i++;
                }
                continue;
            }

            if (char.IsHighSurrogate(text[i]))
            {
                // Surrogate pair — emoji etc., count as 2 display columns
                i += 2;
                len += 2;
            }
            else
            {
                i++;
                len++;
            }
        }
        return len;
    }

    /// <summary>
    /// Formats a TimeSpan as a human-readable duration string.
    /// </summary>
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

    /// <summary>
    /// Word-wraps a string to fit within a given width.
    /// </summary>
    private static List<string> WordWrap(string text, int maxWidth)
    {
        var lines = new List<string>();
        var words = text.Split(' ');
        var current = new StringBuilder();

        foreach (var word in words)
        {
            if (current.Length + word.Length + 1 > maxWidth)
            {
                if (current.Length > 0)
                {
                    lines.Add(current.ToString().TrimEnd());
                    current.Clear();
                }

                // If a single word is too long, just add it
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

/// <summary>
/// Holds session statistics for the Radiance shell.
/// </summary>
public class SessionStats
{
    /// <summary>
    /// The date and time when the session started.
    /// </summary>
    public DateTime SessionStart { get; init; } = DateTime.Now;

    /// <summary>
    /// Total number of commands executed in this session.
    /// </summary>
    public int CommandCount { get; set; }

    /// <summary>
    /// Number of unique command names used in this session.
    /// </summary>
    public int UniqueCommands => CommandFrequency.Count;

    /// <summary>
    /// Frequency map of command names to execution counts.
    /// </summary>
    public Dictionary<string, int> CommandFrequency { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The top 5 most-used commands, sorted by frequency (descending).
    /// </summary>
    public List<(string cmd, int count)> TopCommands =>
        CommandFrequency
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();

    /// <summary>
    /// Records that a command was executed.
    /// </summary>
    /// <param name="commandName">The name of the command that was run.</param>
    public void RecordCommand(string commandName)
    {
        CommandCount++;
        if (CommandFrequency.ContainsKey(commandName))
            CommandFrequency[commandName]++;
        else
            CommandFrequency[commandName] = 1;
    }
}