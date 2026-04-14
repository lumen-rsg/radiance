using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>true</c> command — always returns exit code 0.
/// </summary>
public sealed class TrueCommand : IBuiltinCommand
{
    public string Name => "true";

    public int Execute(string[] args, ShellContext context) => 0;
}