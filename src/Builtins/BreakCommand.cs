using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>break</c> command — exits the current (or nth enclosing) loop.
/// Sets <see cref="ShellContext.BreakRequested"/> and <see cref="ShellContext.BreakDepth"/>
/// flags which are checked by the interpreter's loop visitors.
/// </summary>
public sealed class BreakCommand : IBuiltinCommand
{
    public string Name => "break";

    public int Execute(string[] args, ShellContext context)
    {
        var depth = 1;

        if (args.Length > 1)
        {
            if (!int.TryParse(args[1], out depth) || depth < 1)
            {
                Console.Error.WriteLine($"break: {args[1]}: loop count out of range");
                return 1;
            }
        }

        context.BreakRequested = true;
        context.BreakDepth = depth;
        return 0;
    }
}