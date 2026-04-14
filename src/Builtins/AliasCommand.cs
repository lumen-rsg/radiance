using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// The <c>alias</c> builtin — defines or displays shell aliases.
/// Usage:
///   <c>alias</c> — list all aliases
///   <c>alias name</c> — show a specific alias
///   <c>alias name=value</c> — define an alias
/// </summary>
public sealed class AliasCommand : IBuiltinCommand
{
    /// <inheritdoc/>
    public string Name => "alias";

    /// <inheritdoc/>
    public int Execute(string[] args, ShellContext context)
    {
        if (args.Length == 1)
        {
            // No arguments — list all aliases
            foreach (var (name, value) in context.Aliases)
            {
                Console.WriteLine($"alias {name}='{value}'");
            }
            return 0;
        }

        var exitCode = 0;

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            var eqIdx = arg.IndexOf('=');

            if (eqIdx < 0)
            {
                // No '=' — display the alias
                var value = context.GetAlias(arg);
                if (value is not null)
                {
                    Console.WriteLine($"alias {arg}='{value}'");
                }
                else
                {
                    Console.Error.WriteLine($"radiance: alias: {arg}: not found");
                    exitCode = 1;
                }
            }
            else
            {
                // Define an alias
                var name = arg[..eqIdx];
                var value = arg[(eqIdx + 1)..];
                context.SetAlias(name, value);
            }
        }

        return exitCode;
    }
}