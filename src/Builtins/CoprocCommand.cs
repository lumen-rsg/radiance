using System.Diagnostics;
using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>coproc</c> command — runs a command asynchronously with a bidirectional pipe.
/// Syntax: <c>coproc [NAME] { command; }</c> or <c>coproc [NAME] command args...</c>
/// Stores file descriptors in COPROC array: COPROC[0] (read), COPROC[1] (write), COPROC_PID.
/// </summary>
public sealed class CoprocCommand : IBuiltinCommand
{
    public string Name => "coproc";

    public int Execute(string[] args, ShellContext context)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("radiance: coproc: usage: coproc [NAME] { command; } or coproc [NAME] command [args...]");
            return 2;
        }

        var i = 1;
        string name;

        // Determine coproc name
        if (args[i] == "{" || (!args[i].Contains('=') && !args[i].StartsWith('-') && args.Length > i + 1))
        {
            // If first arg looks like a name (not a command), use it
            if (args[i] != "{" && !LooksLikeCommand(args[i]))
            {
                name = args[i];
                i++;
            }
            else
            {
                name = "COPROC";
            }
        }
        else
        {
            name = "COPROC";
        }

        // Get the command to run
        string commandText;
        if (i < args.Length && args[i] == "{")
        {
            // Brace form: coproc NAME { command; }
            var sb = new System.Text.StringBuilder();
            for (var j = i + 1; j < args.Length; j++)
            {
                if (args[j] == "}") break;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(args[j]);
            }
            commandText = sb.ToString();
        }
        else
        {
            // Command form: coproc NAME command args...
            var sb = new System.Text.StringBuilder();
            for (var j = i; j < args.Length; j++)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(args[j]);
            }
            commandText = sb.ToString();
        }

        if (string.IsNullOrWhiteSpace(commandText))
        {
            Console.Error.WriteLine("radiance: coproc: no command specified");
            return 1;
        }

        try
        {
            // Create temp files for bidirectional pipe
            var inFile = Path.GetTempFileName();
            var outFile = Path.GetTempFileName();

            // Start the coproc process
            var startInfo = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = $"-c {QuoteForShell(commandText)}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                RedirectStandardError = false,
                WorkingDirectory = context.CurrentDirectory
            };

            // Copy exported env vars
            foreach (var envName in context.ExportedVariableNames)
            {
                var envValue = context.GetVariable(envName);
                if (!string.IsNullOrEmpty(envValue))
                    startInfo.Environment[envName] = envValue;
            }

            var process = new Process { StartInfo = startInfo };

            // Start async stdout reader that writes to outFile
            process.Start();

            var pid = process.Id;

            // Start background thread to read stdout and write to outFile
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var output = process.StandardOutput.ReadToEnd();
                    File.WriteAllText(outFile, output);
                    process.WaitForExit();
                    var exitCode = process.ExitCode;
                    File.WriteAllText(inFile + ".done", exitCode.ToString());
                }
                catch
                {
                    // Process may have already exited
                }
            });

            // Write the initial stdin file (empty — user can write to it)
            File.WriteAllText(inFile, "");

            // Store in array variables
            context.SetArrayVariable(name, new List<string> { inFile, outFile });
            context.SetVariable(name + "_PID", pid.ToString());
            context.LastBackgroundPid = pid;

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"radiance: coproc: {ex.Message}");
            return 1;
        }
    }

    private static bool LooksLikeCommand(string arg)
    {
        // If it contains a path separator or dot, it's probably a command
        return arg.Contains('/') || arg.Contains('.') || arg.Contains('=');
    }

    private static string QuoteForShell(string text)
    {
        return "'" + text.Replace("'", "'\\''") + "'";
    }
}
