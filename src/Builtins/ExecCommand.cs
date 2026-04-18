using Radiance.Interpreter;
using Radiance.Utils;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>exec</c> command — replaces the shell with the given command.
/// <list type="bullet">
/// <item><c>exec</c> (no args) — no-op, returns 0</item>
/// <item><c>exec command args...</c> — executes the command, then exits the shell</item>
/// </list>
/// Since .NET cannot truly replace the process (no execve), we simulate by
/// executing the command and then terminating the shell with its exit code.
/// </summary>
public sealed class ExecCommand : IBuiltinCommand
{
    /// <summary>
    /// Fired when <c>exec</c> successfully runs a command and the shell should exit.
    /// The event argument is the exit code from the executed command.
    /// </summary>
    public static event EventHandler<int>? ExecRequested;

    public string Name => "exec";

    public int Execute(string[] args, ShellContext context)
    {
        if (args.Length <= 1)
        {
            // No arguments — no-op per POSIX
            return 0;
        }

        // Reconstruct the command string from args (skip "exec")
        var commandParts = new string[args.Length - 1];
        Array.Copy(args, 1, commandParts, 0, args.Length - 1);

        var commandLine = string.Join(" ", commandParts);

        // Execute via the command line executor callback
        if (context.CommandLineExecutor is not null)
        {
            var exitCode = context.CommandLineExecutor(commandLine);
            ExecRequested?.Invoke(this, exitCode);
            return exitCode;
        }

        ColorOutput.WriteError("exec: no command executor available");
        return 1;
    }
}