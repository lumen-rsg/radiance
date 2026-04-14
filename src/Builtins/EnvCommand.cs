using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>env</c> command — prints all exported environment variables.
/// </summary>
public sealed class EnvCommand : IBuiltinCommand
{
    public string Name => "env";

    public int Execute(string[] args, ShellContext context)
    {
        var vars = Environment.GetEnvironmentVariables();
        var keys = new List<string>();

        foreach (System.Collections.DictionaryEntry? entry in vars)
        {
            if (entry is not null)
                keys.Add((string)entry.Value.Key);
        }

        keys.Sort();

        foreach (var key in keys)
        {
            var value = Environment.GetEnvironmentVariable(key);
            Console.WriteLine($"{key}={value}");
        }

        return 0;
    }
}