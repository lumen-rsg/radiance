using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>pwd</c> command — prints the current working directory.
/// </summary>
public sealed class PwdCommand : IBuiltinCommand
{
    public string Name => "pwd";

    public int Execute(string[] args, ShellContext context)
    {
        Console.WriteLine(context.CurrentDirectory);
        return 0;
    }
}