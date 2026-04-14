using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>set</c> command — lists all shell variables, or sets flags.
/// Without arguments, prints all variables (name=value format).
/// </summary>
public sealed class SetCommand : IBuiltinCommand
{
    public string Name => "set";

    public int Execute(string[] args, ShellContext context)
    {
        if (args.Length <= 1)
        {
            // Print all shell variables
            foreach (var name in context.ShellVariableNames.OrderBy(n => n))
            {
                var value = context.GetVariable(name);
                Console.WriteLine($"{name}={value}");
            }

            return 0;
        }

        // Handle set -e, set +e, etc. in future phases
        return 0;
    }
}