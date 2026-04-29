using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>export</c> command — marks shell variables for export to child processes.
/// Supports <c>export NAME=VALUE</c> and <c>export NAME</c> forms.
/// </summary>
public sealed class ExportCommand : IBuiltinCommand
{
    public string Name => "export";

    public int Execute(string[] args, ShellContext context)
    {
        if (args.Length <= 1)
        {
            // No arguments: list all exported variables
            foreach (var name in context.ExportedVariableNames.OrderBy(n => n))
            {
                var variable = context.GetShellVariable(name);
                var value = variable?.Value ?? string.Empty;
                Console.WriteLine($"declare -x {name}=\"{value}\"");
            }

            return 0;
        }

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];

            var eqIdx = arg.IndexOf('=');
            if (eqIdx >= 0)
            {
                var name = arg[..eqIdx];
                var value = arg[(eqIdx + 1)..];
                context.ExportVariable(name, value);
            }
            else
            {
                context.ExportVariable(arg);
            }
        }

        return 0;
    }
}