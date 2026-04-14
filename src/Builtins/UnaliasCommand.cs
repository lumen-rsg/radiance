using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// The <c>unalias</c> builtin — removes shell aliases.
/// Usage:
///   <c>unalias name</c> — remove a specific alias
///   <c>unalias -a</c> — remove all aliases
/// </summary>
public sealed class UnaliasCommand : IBuiltinCommand
{
    /// <inheritdoc/>
    public string Name => "unalias";

    /// <inheritdoc/>
    public int Execute(string[] args, ShellContext context)
    {
        if (args.Length == 1)
        {
            Console.Error.WriteLine("radiance: unalias: usage: unalias name [-a]");
            return 2;
        }

        var exitCode = 0;

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "-a")
            {
                context.UnsetAllAliases();
                return 0;
            }

            if (context.GetAlias(arg) is not null)
            {
                context.UnsetAlias(arg);
            }
            else
            {
                Console.Error.WriteLine($"radiance: unalias: {arg}: not found");
                exitCode = 1;
            }
        }

        return exitCode;
    }
}