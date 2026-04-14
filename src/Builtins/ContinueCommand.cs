using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>continue</c> command — skips to the next iteration of the current
/// (or nth enclosing) loop. Sets <see cref="ShellContext.ContinueRequested"/> and
/// <see cref="ShellContext.ContinueDepth"/> flags checked by the interpreter's loop visitors.
/// </summary>
public sealed class ContinueCommand : IBuiltinCommand
{
    public string Name => "continue";

    public int Execute(string[] args, ShellContext context)
    {
        var depth = 1;

        if (args.Length > 1)
        {
            if (!int.TryParse(args[1], out depth) || depth < 1)
            {
                Console.Error.WriteLine($"continue: {args[1]}: loop count out of range");
                return 1;
            }
        }

        context.ContinueRequested = true;
        context.ContinueDepth = depth;
        return 0;
    }
}