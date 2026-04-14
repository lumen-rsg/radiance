using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>unset</c> command — removes shell variables.
/// </summary>
public sealed class UnsetCommand : IBuiltinCommand
{
    public string Name => "unset";

    public int Execute(string[] args, ShellContext context)
    {
        if (args.Length <= 1)
        {
            return 0;
        }

        for (var i = 1; i < args.Length; i++)
        {
            context.UnsetVariable(args[i]);
        }

        return 0;
    }
}