using Radiance.Shell;
using Radiance.Utils;

namespace Radiance;

/// <summary>
/// Entry point for the Radiance shell.
/// Supports interactive mode, script file execution, and inline command execution.
/// </summary>
/// <remarks>
/// Usage:
/// <list type="bullet">
/// <item><c>radiance</c> — launch interactive REPL</item>
/// <item><c>radiance script.sh [args...]</c> — execute a script file</item>
/// <item><c>radiance -c "command" [args...]</c> — execute an inline command</item>
/// <item><c>radiance --help</c> — show usage information</item>
/// <item><c>radiance --version</c> — show version</item>
/// </list>
/// </remarks>
public static class Program
{
    private const string Version = "0.7.0";

    public static int Main(string[] args)
    {
        // Handle --help and --version
        if (args.Length > 0)
        {
            switch (args[0])
            {
                case "--help":
                case "-h":
                    PrintUsage();
                    return 0;

                case "--version":
                case "-v":
                    Console.WriteLine($"Radiance Shell v{Version}");
                    return 0;
            }
        }

        // Handle -c "command" mode
        if (args.Length > 0 && args[0] == "-c")
        {
            if (args.Length < 2)
            {
                ColorOutput.WriteError("-c: option requires an argument");
                return 2;
            }

            var command = args[1];
            var commandArgs = new List<string> { "radiance" };

            // Additional arguments after the command string become positional params
            for (var i = 2; i < args.Length; i++)
            {
                if (args[i] == "--")
                {
                    // Everything after -- is positional params
                    for (var j = i + 1; j < args.Length; j++)
                        commandArgs.Add(args[j]);
                    break;
                }
                commandArgs.Add(args[i]);
            }

            var shell = new RadianceShell();
            return shell.ExecuteString(command, commandArgs.ToArray());
        }

        // Handle script file execution
        if (args.Length > 0 && !args[0].StartsWith('-'))
        {
            var scriptPath = args[0];

            if (!File.Exists(scriptPath))
            {
                ColorOutput.WriteError($"{scriptPath}: No such file or directory");
                return 127;
            }

            // Build script arguments: $0 = script path, $1..$n = remaining args
            var scriptArgs = new List<string> { scriptPath };
            for (var i = 1; i < args.Length; i++)
            {
                scriptArgs.Add(args[i]);
            }

            var shell = new RadianceShell();
            return shell.ExecuteScript(scriptPath, scriptArgs.ToArray());
        }

        // Interactive REPL mode
        var interactiveShell = new RadianceShell();
        return interactiveShell.Run();
    }

    /// <summary>
    /// Prints usage information to stdout.
    /// </summary>
    private static void PrintUsage()
    {
        Console.WriteLine($"""
            Radiance Shell v{Version} — A BASH interpreter in C#

            Usage:
              radiance                    Launch interactive REPL
              radiance script.sh [args]   Execute a script file
              radiance -c "command"       Execute an inline command
              radiance --help             Show this help message
              radiance --version          Show version

            Options:
              -c <command>    Execute the given command string
              -h, --help      Show this help message
              -v, --version   Show version information
            """);
    }
}