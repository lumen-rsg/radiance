using System.Net.Sockets;
using Radiance.Multiplexer;
using Radiance.Shell;
using Radiance.Terminal;
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

        // Handle --mux multiplexer mode
        if (args.Length > 0 && args[0] == "--mux")
        {
            return RunMultiplexer(args[1..]);
        }

        // Handle --mux-daemon (internal: spawned by --mux new)
        if (args.Length > 0 && args[0] == "--mux-daemon")
        {
            if (args.Length < 3)
            {
                ColorOutput.WriteError("--mux-daemon: session name and command required");
                return 1;
            }
            return RunMuxDaemon(args[1], args[2]);
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
              radiance --mux              Start terminal multiplexer
              radiance --mux new          Start multiplexer with session name
              radiance --help             Show this help message
              radiance --version          Show version information

            Options:
              -l, --login     Run as a login shell (sources /etc/profile, ~/.bash_profile)
              -c <command>    Execute the given command string
              --mux           Start tmux-like terminal multiplexer
              -h, --help      Show this help message
              -v, --version   Show version information
            """);
    }

    /// <summary>
    /// Run the terminal multiplexer.
    /// </summary>
    private static int RunMultiplexer(string[] muxArgs)
    {
        var subcommand = muxArgs.Length > 0 ? muxArgs[0] : "new";

        switch (subcommand)
        {
            case "new":
            {
                var sessionName = muxArgs.Length > 1 && !muxArgs[1].StartsWith('-')
                    ? muxArgs[1]
                    : $"radiance-{Environment.GetEnvironmentVariable("USER") ?? "user"}";

                var shell = Environment.GetEnvironmentVariable("SHELL") ?? "/bin/sh";

                // Start the daemon as a background process
                var daemonExe = Environment.ProcessPath;
                if (daemonExe is null)
                {
                    ColorOutput.WriteError("Cannot determine executable path for daemon");
                    return 1;
                }

                // Check if a session with this name already exists
                var existingSocket = MuxSessionDir.SocketPath(sessionName);
                if (File.Exists(existingSocket))
                {
                    // Try to connect — if it fails, the socket is stale
                    try
                    {
                        using var testSock = new System.Net.Sockets.Socket(
                            System.Net.Sockets.AddressFamily.Unix,
                            System.Net.Sockets.SocketType.Stream,
                            System.Net.Sockets.ProtocolType.Unspecified);
                        testSock.Connect(new UnixDomainSocketEndPoint(existingSocket));
                        testSock.Close();
                        // Connection succeeded — session is alive
                        ColorOutput.WriteError($"Session '{sessionName}' already exists. Use 'radiance --mux attach {sessionName}' to connect.");
                        return 1;
                    }
                    catch
                    {
                        // Stale socket — clean it up
                        try { File.Delete(existingSocket); } catch { }
                    }
                }

                // Spawn daemon process: radiance --mux-daemon <name> <shell>
                using var daemon = new System.Diagnostics.Process();
                daemon.StartInfo.FileName = daemonExe;
                daemon.StartInfo.Arguments = $"--mux-daemon \"{sessionName}\" \"{shell}\"";
                daemon.StartInfo.UseShellExecute = false;
                daemon.StartInfo.CreateNoWindow = true;

                if (!daemon.Start())
                {
                    ColorOutput.WriteError("Failed to start mux daemon");
                    return 1;
                }

                // Give the daemon a moment to create the socket
                System.Threading.Thread.Sleep(200);

                // Connect as a client
                using var client = new MuxClient(sessionName);
                return client.Run();
            }

            case "ls":
            {
                var sessions = MuxSessionDir.ListSessions();
                if (sessions.Count == 0)
                {
                    Console.WriteLine("No active sessions.");
                    return 0;
                }

                foreach (var name in sessions)
                {
                    var infoPath = MuxSessionDir.InfoPath(name);
                    string details = "";
                    if (File.Exists(infoPath))
                    {
                        var lines = File.ReadAllLines(infoPath);
                        if (lines.Length >= 3)
                        {
                            var pid = lines[0];
                            var created = DateTime.Parse(lines[2], null, System.Globalization.DateTimeStyles.RoundtripKind);
                            details = $" (pid {pid}, created {created:HH:mm:ss})";
                        }
                    }
                    Console.WriteLine($"  {name}{details}");
                }
                return 0;
            }

            case "kill-session":
            {
                if (muxArgs.Length < 2)
                {
                    ColorOutput.WriteError("kill-session: session name required");
                    return 1;
                }

                var targetName = muxArgs[1];
                var infoPath = MuxSessionDir.InfoPath(targetName);
                if (!File.Exists(infoPath))
                {
                    ColorOutput.WriteError($"No session '{targetName}' found.");
                    return 1;
                }

                var lines = File.ReadAllLines(infoPath);
                if (lines.Length > 0 && int.TryParse(lines[0], out var pid))
                {
                    try
                    {
                        System.Diagnostics.Process.GetProcessById(pid)?.Kill();
                        Console.WriteLine($"Killed session '{targetName}' (pid {pid}).");
                    }
                    catch (Exception ex)
                    {
                        ColorOutput.WriteError($"Failed to kill session: {ex.Message}");
                    }
                }

                MuxSessionDir.Cleanup(targetName);
                return 0;
            }

            case "attach":
            {
                var attachName = muxArgs.Length > 1 ? muxArgs[1]
                    : $"radiance-{Environment.GetEnvironmentVariable("USER") ?? "user"}";

                using var client = new MuxClient(attachName);
                return client.Run();
            }

            default:
                ColorOutput.WriteError($"Unknown mux subcommand: {subcommand}");
                Console.WriteLine("Usage: radiance --mux [new [name] | ls | kill-session | attach [name]]");
                return 1;
        }
    }

    /// <summary>
    /// Run as a mux daemon (background process). Called internally by --mux new.
    /// </summary>
    private static int RunMuxDaemon(string sessionName, string command)
    {
        using var daemon = new MuxDaemon(sessionName,
            Console.WindowWidth > 0 ? Console.WindowWidth : 120,
            Console.WindowHeight > 0 ? Console.WindowHeight : 40,
            command);

        daemon.Run();
        return 0;
    }
}
