namespace Radiance.Builtins;

/// <summary>
/// The <c>return</c> builtin — exits a function with an optional exit code.
/// Usage: <c>return [n]</c>
/// Sets <see cref="ShellContext.ReturnRequested"/> to true and stores the exit code.
/// </summary>
public sealed class ReturnCommand : IBuiltinCommand
{
    /// <inheritdoc/>
    public string Name => "return";

    /// <inheritdoc/>
    public int Execute(string[] args, ShellContext context)
    {
        var exitCode = 0;

        if (args.Length > 1)
        {
            if (int.TryParse(args[1], out var code))
            {
                exitCode = code;
            }
            else
            {
                Console.Error.WriteLine($"radiance: return: {args[1]}: numeric argument required");
                exitCode = 2;
            }
        }
        else
        {
            // Use the last exit code as the return value
            exitCode = context.LastExitCode;
        }

        context.ReturnRequested = true;
        context.ReturnExitCode = exitCode;
        return exitCode;
    }
}