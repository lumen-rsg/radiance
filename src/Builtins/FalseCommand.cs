using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>false</c> command — always returns exit code 1.
/// </summary>
public sealed class FalseCommand : IBuiltinCommand
{
    public string Name => "false";

    public int Execute(string[] args, ShellContext context) => 1;
}