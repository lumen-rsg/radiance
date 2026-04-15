using Radiance.Shell;
using Radiance.Utils;

namespace Radiance;

/// <summary>
/// Entry point for the Radiance shell.
/// Supports interactive mode, login shell mode, script file execution, and inline command execution.
/// </summary>
/// <remarks>
/// Usage:
/// <list type="bullet">
/// <item><c>radiance</c> — launch interactive REPL</item>
/// <item><c>radiance -l</c> / <c>radiance --login</c> — launch as login shell</item>
/// <item><c>radiance script.sh [args...]</c> — execute a script file</item>
/// <item><c>radiance -c "command" [args...]</c> — execute an inline command</item>
/// <item><c>radiance --help</c> — show usage information</item>
/// <item><c>radiance --version</c> — show version</item>
/// </list>
/// </remarks>
public static class Program
{
    private const string Version = "1.2.3";

    public static int Main(string[] args)
    {
        var isLoginShell = false;

        // Detect login shell: argv[0] starts with '-' (e.g., -radiance) or -l/--login flag
        if (args.Length == 0 && Environment.GetCommandLineArgs().Length > 0)
        {
            var arg0 = Environment.GetCommandLineArgs()[0];
            var exeName = Path.GetFileName(arg0);
            if (exeName.StartsWith('-'))
            {
                isLoginShell = true;
            }
        }

        // Parse flags
        var filteredArgs = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help":
                case "-h":
                    PrintUsage();
                    return 0;

                case "--version":
                case "-v":
                    Console.WriteLine($"Radiance Shell v{Version}");
                    return 0;

                case "--login":
                case "-l":
                    isLoginShell = true;
                    break;

                default:
                    // Handle combined flags like -il, -lv, etc.
                    if (args[i].Length > 1 && args[i][0] == '-' && args[i][1] != '-')
                    {
                        var flags = args[i][1..];
                        var consumed = false;
                        foreach (var flag in flags)
                        {
                            switch (flag)
                            {
                                case 'l':
                                    isLoginShell = true;
                                    consumed = true;
                                    break;
                                case 'h':
                                    PrintUsage();
                                    return 0;
                                case 'v':
                                    Console.WriteLine($"Radiance Shell v{Version}");
                                    return 0;
                            }
                        }

                        if (consumed && flags.All(f => f is 'l' or 'i'))
                        {
                            // All flags were shell mode flags, don't pass through
                            break;
                        }
                    }

                    filteredArgs.Add(args[i]);
                    break;
            }
        }

        args = filteredArgs.ToArray();

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

            var shell = new RadianceShell(isLoginShell);
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

            var shell = new RadianceShell(false);
            return shell.ExecuteScript(scriptPath, scriptArgs.ToArray());
        }

        // Interactive REPL mode
        var interactiveShell = new RadianceShell(isLoginShell);
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
              radiance -l, --login        Launch as a login shell
              radiance script.sh [args]   Execute a script file
              radiance -c "command"       Execute an inline command
              radiance --help             Show this help message
              radiance --version          Show version information

            Options:
              -l, --login     Run as a login shell (sources /etc/profile, ~/.bash_profile)
              -c <command>    Execute the given command string
              -h, --help      Show this help message
              -v, --version   Show version information
            """);
    }
}
