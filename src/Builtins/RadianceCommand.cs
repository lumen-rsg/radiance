using Radiance.Interpreter;
using Radiance.Utils;

namespace Radiance.Builtins;

/// <summary>
/// The <c>radiance</c> builtin command — a signature showcase command with
/// sparkle effects, ASCII art, fortune cookies, matrix rain, and session stats.
/// </summary>
/// <remarks>
/// Usage:
/// <list type="bullet">
///   <item><c>radiance</c> — Show the Radiance ASCII art logo with gradient colors</item>
///   <item><c>radiance spark</c> — Trigger sparkle mode animation</item>
///   <item><c>radiance fortune</c> — Display a random nerdy fortune cookie</item>
///   <item><c>radiance stats</c> — Show session statistics dashboard</item>
///   <item><c>radiance matrix</c> — Matrix-style digital rain animation</item>
///   <item><c>radiance help</c> — Show usage information</item>
/// </list>
/// </remarks>
public sealed class RadianceCommand : IBuiltinCommand
{
    /// <inheritdoc />
    public string Name => "radiance";

    /// <summary>
    /// The session stats instance to use for the <c>stats</c> subcommand.
    /// Must be set before the command is executed.
    /// </summary>
    public SessionStats? Stats { get; set; }

    /// <summary>
    /// The shell version string to display in the logo.
    /// Must be set before the command is executed.
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    /// <inheritdoc />
    public int Execute(string[] args, ShellContext context)
    {
        var subcommand = args.Length > 1 ? args[1].ToLowerInvariant() : "";

        switch (subcommand)
        {
            case "":
                // No args — show the gorgeous ASCII art logo
                SparkleRenderer.RenderLogo(Version);
                return 0;

            case "spark":
            case "sparkle":
                // Sparkle mode!
                Console.WriteLine("\x1b[1;33m  ✦ Initiating sparkle mode... ✦\x1b[0m");
                Console.WriteLine();
                SparkleRenderer.RenderSparkle();
                return 0;

            case "fortune":
                // Random fortune cookie
                SparkleRenderer.RenderFortune();
                return 0;

            case "stats":
                // Session statistics dashboard
                if (Stats is null)
                {
                    Console.WriteLine("\x1b[1;31m  radiance: stats not available (non-interactive mode)\x1b[0m");
                    return 1;
                }
                SparkleRenderer.RenderStats(Stats);
                return 0;

            case "matrix":
                // Matrix rain animation
                Console.WriteLine("\x1b[1;32m  Initializing the Matrix...\x1b[0m");
                SparkleRenderer.RenderMatrix();
                return 0;

            case "help":
            case "--help":
            case "-h":
                PrintHelp();
                return 0;

            default:
                Console.WriteLine($"\x1b[1;31m  radiance: unknown subcommand '{args[1]}'\x1b[0m");
                Console.WriteLine("\x1b[37m  Try: radiance help\x1b[0m");
                return 1;
        }
    }

    /// <summary>
    /// Prints usage information for the <c>radiance</c> command.
    /// </summary>
    private static void PrintHelp()
    {
        Console.WriteLine("\x1b[1;36m  ╭────── radiance ──────╮\x1b[0m");
        Console.WriteLine("\x1b[1;36m  │\x1b[0m                       \x1b[1;36m│\x1b[0m");
        Console.WriteLine("\x1b[1;36m  │\x1b[0m  \x1b[1;33mUsage:\x1b[0m                \x1b[1;36m│\x1b[0m");
        Console.WriteLine("\x1b[1;36m  │\x1b[0m    radiance [command]   \x1b[1;36m│\x1b[0m");
        Console.WriteLine("\x1b[1;36m  │\x1b[0m                       \x1b[1;36m│\x1b[0m");
        Console.WriteLine("\x1b[1;36m  │\x1b[0m  \x1b[1;33mCommands:\x1b[0m             \x1b[1;36m│\x1b[0m");
        Console.WriteLine("\x1b[1;36m  │\x1b[0m    (none)   Show logo   \x1b[1;36m│\x1b[0m");
        Console.WriteLine("\x1b[1;36m  │\x1b[0m    spark    Sparkle!    \x1b[1;36m│\x1b[0m");
        Console.WriteLine("\x1b[1;36m  │\x1b[0m    fortune  Fortune 🍪   \x1b[1;36m│\x1b[0m");
        Console.WriteLine("\x1b[1;36m  │\x1b[0m    stats    Session info \x1b[1;36m│\x1b[0m");
        Console.WriteLine("\x1b[1;36m  │\x1b[0m    matrix   Enter Matrix \x1b[1;36m│\x1b[0m");
        Console.WriteLine("\x1b[1;36m  │\x1b[0m    help     This message \x1b[1;36m│\x1b[0m");
        Console.WriteLine("\x1b[1;36m  │\x1b[0m                       \x1b[1;36m│\x1b[0m");
        Console.WriteLine("\x1b[1;36m  ╰───────────────────────╯\x1b[0m");
        Console.WriteLine();
    }
}