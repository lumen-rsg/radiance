using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>exit</c> command — exits the shell with an optional exit code.
/// </summary>
public sealed class ExitCommand : IBuiltinCommand
{
    /// <summary>
    /// Fired when the user requests shell exit.
    /// </summary>
    public static event EventHandler<int>? ExitRequested;

    public string Name => "exit";

    public int Execute(string[] args, ShellContext context)
    {
        var exitCode = context.LastExitCode;

        if (args.Length > 1 && int.TryParse(args[1], out var userCode))
        {
            exitCode = userCode;
        }

        ExitRequested?.Invoke(this, exitCode);
        return exitCode;
    }
}