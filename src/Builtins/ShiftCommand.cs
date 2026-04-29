using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>shift</c> command — shifts positional parameters left by N.
/// Removes the first N positional parameters, shifting the remaining ones down.
/// Without arguments, shifts by 1.
/// </summary>
public sealed class ShiftCommand : IBuiltinCommand
{
    public string Name => "shift";

    public int Execute(string[] args, ShellContext context)
    {
        var n = 1;

        if (args.Length > 1)
        {
            if (!int.TryParse(args[1], out n) || n < 0)
            {
                Console.Error.WriteLine($"radiance: shift: {args[1]}: shift count must be a non-negative integer");
                return 1;
            }
        }

        if (n > context.PositionalParamCount)
        {
            Console.Error.WriteLine($"radiance: shift: {n}: shift count out of range");
            return 1;
        }

        var currentParams = context.PositionalParams.ToList();
        var newParams = currentParams.Skip(n).ToList();
        context.SetPositionalParams(newParams);

        return 0;
    }
}
