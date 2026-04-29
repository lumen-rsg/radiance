using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>readonly</c> command — marks variables as read-only.
/// Without arguments, lists all read-only variables.
/// </summary>
public sealed class ReadonlyCommand : IBuiltinCommand
{
    public string Name => "readonly";

    public int Execute(string[] args, ShellContext context)
    {
        if (args.Length <= 1)
        {
            // List all read-only variables
            foreach (var name in context.ShellVariableNames.OrderBy(n => n))
            {
                var variable = context.GetShellVariable(name);
                if (variable is not null && variable.IsReadOnly)
                    Console.WriteLine($"declare -r {name}=\"{variable.Value}\"");
            }

            return 0;
        }

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];

            // Handle -p flag (print read-only variables)
            if (arg == "-p")
            {
                foreach (var name in context.ShellVariableNames.OrderBy(n => n))
                {
                    var variable = context.GetShellVariable(name);
                    if (variable is not null && variable.IsReadOnly)
                        Console.WriteLine($"declare -r {name}=\"{variable.Value}\"");
                }

                continue;
            }

            var eqIdx = arg.IndexOf('=');
            if (eqIdx >= 0)
            {
                var name = arg[..eqIdx];
                var value = arg[(eqIdx + 1)..];
                var variable = context.GetShellVariable(name)?.Clone() ?? new ShellVariable();
                variable.Value = value;
                variable.IsReadOnly = true;
                context.SetShellVariable(name, variable);
            }
            else
            {
                // Mark existing variable as read-only
                var existing = context.GetShellVariable(arg);
                if (existing is not null)
                {
                    existing.IsReadOnly = true;
                }
                else
                {
                    // Create a new empty read-only variable
                    context.SetShellVariable(arg, new ShellVariable { IsReadOnly = true });
                }
            }
        }

        return 0;
    }
}
