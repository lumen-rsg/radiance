using Radiance.Interpreter;
using Radiance.Terminal;
using Radiance.Utils;

namespace Radiance.Builtins;

/// <summary>
/// The <c>radiance</c> builtin command — a signature showcase command with
/// sparkle effects, ASCII art, fortune cookies, matrix rain, and session stats.
/// Uses the DaVinci rendering system for rich terminal views.
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

    /// <summary>
    /// The DaVinci renderer for rich terminal views.
    /// Must be set before the command is executed.
    /// </summary>
    public DaVinciRenderer? DaVinci { get; set; }

    /// <inheritdoc />
    public int Execute(string[] args, ShellContext context)
    {
        var subcommand = args.Length > 1 ? args[1].ToLowerInvariant() : "";

        // If DaVinci is not available or terminal is redirected, fall back to SparkleRenderer
        if (DaVinci is null || Console.IsOutputRedirected)
        {
            return ExecuteFallback(subcommand);
        }

        try
        {
            switch (subcommand)
            {
                case "":
                    DaVinci.ShowStaticView((buf, w, h) =>
                        DaVinciViewBuilder.RenderWelcomeBanner(buf, w, h, Version));
                    return 0;

                case "spark":
                case "sparkle":
                    DaVinciViewBuilder.ResetSparkles();
                    DaVinci.RunAnimation(DaVinciViewBuilder.RenderSparkleAnimation, 1500);
                    Console.WriteLine("\x1b[1;33m✦ Sparkle mode complete! ✦\x1b[0m");
                    return 0;

                case "fortune":
                    DaVinci.ShowStaticView(DaVinciViewBuilder.RenderFortune);
                    return 0;

                case "stats":
                    if (Stats is null)
                    {
                        Console.WriteLine("\x1b[1;31m  radiance: stats not available (non-interactive mode)\x1b[0m");
                        return 1;
                    }
                    DaVinci.ShowStaticView((buf, w, h) =>
                        DaVinciViewBuilder.RenderStatsDashboard(buf, w, h, Stats));
                    return 0;

                case "matrix":
                    DaVinciViewBuilder.ResetMatrix();
                    DaVinci.RunAnimation(DaVinciViewBuilder.RenderMatrixAnimation, 2000);
                    Console.WriteLine($"\x1b[1;32mWake up, Radiance... The Matrix has you.\x1b[0m");
                    Console.WriteLine();
                    return 0;

                case "help":
                case "--help":
                case "-h":
                    DaVinci.ShowStaticView(DaVinciViewBuilder.RenderHelpScreen);
                    return 0;

                default:
                    Console.WriteLine($"\x1b[1;31m  radiance: unknown subcommand '{args[1]}'\x1b[0m");
                    Console.WriteLine("\x1b[37m  Try: radiance help\x1b[0m");
                    return 1;
            }
        }
        catch
        {
            // Fall back to SparkleRenderer if DaVinci fails
            return ExecuteFallback(subcommand);
        }
    }

    /// <summary>
    /// Fallback execution using SparkleRenderer when DaVinci is unavailable.
    /// </summary>
    private int ExecuteFallback(string subcommand)
    {
        switch (subcommand)
        {
            case "":
                SparkleRenderer.RenderLogo(Version);
                return 0;

            case "spark":
            case "sparkle":
                Console.WriteLine("\x1b[1;33m  ✦ Initiating sparkle mode... ✦\x1b[0m");
                Console.WriteLine();
                SparkleRenderer.RenderSparkle();
                return 0;

            case "fortune":
                SparkleRenderer.RenderFortune();
                return 0;

            case "stats":
                if (Stats is null)
                {
                    Console.WriteLine("\x1b[1;31m  radiance: stats not available (non-interactive mode)\x1b[0m");
                    return 1;
                }
                SparkleRenderer.RenderStats(Stats);
                return 0;

            case "matrix":
                Console.WriteLine("\x1b[1;32m  Initializing the Matrix...\x1b[0m");
                SparkleRenderer.RenderMatrix();
                return 0;

            case "help":
            case "--help":
            case "-h":
                PrintHelp();
                return 0;

            default:
                Console.WriteLine($"\x1b[1;31m  radiance: unknown subcommand '{subcommand}'\x1b[0m");
                Console.WriteLine("\x1b[37m  Try: radiance help\x1b[0m");
                return 1;
        }
    }

    /// <summary>
    /// Prints usage information for the <c>radiance</c> command.
    /// </summary>
    private static void PrintHelp()
    {
        const int W = 26; // Inner width between │ borders
        var hLine = new string('─', W);

        Console.WriteLine($"\x1b[1;36m  ╭{new string('─', 7)}  \x1b[1;33mradiance\x1b[1;36m  {new string('─', 7)}╮\x1b[0m");
        SparkleRenderer.BoxLine(W, "");
        SparkleRenderer.BoxLine(W, $"  \x1b[1;33mUsage:\x1b[0m");
        SparkleRenderer.BoxLine(W, $"    radiance [command]");
        SparkleRenderer.BoxLine(W, "");
        SparkleRenderer.BoxLine(W, $"  \x1b[1;33mCommands:\x1b[0m");
        SparkleRenderer.BoxLine(W, $"    (none)   Show logo");
        SparkleRenderer.BoxLine(W, $"    spark    Sparkle!");
        SparkleRenderer.BoxLine(W, $"    fortune  Fortune");
        SparkleRenderer.BoxLine(W, $"    stats    Session info");
        SparkleRenderer.BoxLine(W, $"    matrix   Enter Matrix");
        SparkleRenderer.BoxLine(W, $"    help     This message");
        SparkleRenderer.BoxLine(W, "");
        Console.WriteLine($"\x1b[1;36m  ╰{hLine}╯\x1b[0m");
        Console.WriteLine();
    }
}
