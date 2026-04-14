using Radiance.Shell;

namespace Radiance;

/// <summary>
/// Entry point for the Radiance shell.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        // If args are provided, treat them as a command to execute (non-interactive)
        if (args.Length > 0)
        {
            // Future: script file execution (Phase 7)
            // For now, just launch interactive mode
            Console.WriteLine($"radiance: script execution not yet supported");
            return 1;
        }

        // Launch interactive REPL
        var shell = new RadianceShell();
        return shell.Run();
    }
}