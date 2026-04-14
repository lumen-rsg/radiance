using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// The <c>local</c> builtin — declares local variables inside functions.
/// Usage: <c>local VAR=value</c> or <c>local VAR</c>
/// Variables are set in the current function scope.
/// </summary>
public sealed class LocalCommand : IBuiltinCommand
{
    /// <inheritdoc/>
    public string Name => "local";

    /// <inheritdoc/>
    public int Execute(string[] args, ShellContext context)
    {
        if (context.ScopeDepth <= 1)
        {
            Console.Error.WriteLine("radiance: local: can only be used in a function");
            return 1;
        }

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            var eqIdx = arg.IndexOf('=');

            if (eqIdx >= 0)
            {
                var name = arg[..eqIdx];
                var value = arg[(eqIdx + 1)..];
                context.SetLocalVariable(name, value);
            }
            else
            {
                // Declare without value — set to empty string
                context.SetLocalVariable(arg, string.Empty);
            }
        }

        return 0;
    }
}