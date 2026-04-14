using Radiance.Interpreter;
using Radiance.Utils;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>type</c> command — indicates how each name would be interpreted
/// if used as a command name (builtin, alias, function, file, etc.).
/// </summary>
public sealed class TypeCommand : IBuiltinCommand
{
    private BuiltinRegistry _registry;

    public TypeCommand()
    {
        _registry = null!;
    }

    internal void SetRegistry(BuiltinRegistry registry) => _registry = registry;

    public string Name => "type";

    public int Execute(string[] args, ShellContext context)
    {
        if (args.Length <= 1)
        {
            return 0;
        }

        var exitCode = 0;

        for (var i = 1; i < args.Length; i++)
        {
            var name = args[i];

            // Check in BASH order: alias → function → builtin → file
            var alias = context.GetAlias(name);
            if (alias is not null)
            {
                Console.WriteLine($"{name} is aliased to '{alias}'");
            }
            else if (context.HasFunction(name))
            {
                Console.WriteLine($"{name} is a function");
            }
            else if (_registry.IsBuiltin(name))
            {
                Console.WriteLine($"{name} is a shell builtin");
            }
            else
            {
                var resolved = PathResolver.Resolve(name);
                if (resolved is not null)
                {
                    Console.WriteLine($"{name} is {resolved}");
                }
                else
                {
                    Console.Error.WriteLine($"type: {name}: not found");
                    exitCode = 1;
                }
            }
        }

        return exitCode;
    }
}