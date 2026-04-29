using Radiance.Interpreter;
using Radiance.Utils;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>hash</c> command — manages the command lookup cache.
/// <list type="bullet">
/// <item><c>hash</c> — list all hashed commands</item>
/// <item><c>hash name</c> — add command to hash table</item>
/// <item><c>hash -r</c> — clear the hash table</item>
/// </list>
/// </summary>
public sealed class HashCommand : IBuiltinCommand
{
    public string Name => "hash";

    public int Execute(string[] args, ShellContext context)
    {
        if (args.Length <= 1)
        {
            // List hashed commands
            // (Currently using PathResolver's built-in cache — no separate hash table)
            return 0;
        }

        if (args[1] == "-r")
        {
            // Clear the PATH cache
            PathResolver.InvalidateCache();
            return 0;
        }

        // Hash specific commands (add to cache by resolving them)
        for (var i = 1; i < args.Length; i++)
        {
            var resolved = PathResolver.Resolve(args[i]);
            if (resolved is null)
            {
                Console.Error.WriteLine($"radiance: hash: {args[i]}: not found");
                return 1;
            }
        }

        return 0;
    }
}
