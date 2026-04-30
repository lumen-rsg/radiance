using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// The <c>agent</c> builtin command — stub for the planned multi-agent orchestration system.
/// </summary>
public sealed class AgentCommand : IBuiltinCommand
{
    public string Name => "agent";

    public int Execute(string[] args, ShellContext context)
    {
        Console.WriteLine("\x1b[1;33mAgent system is not yet available.\x1b[0m");
        Console.WriteLine("The multi-agent orchestration system is under development.");
        return 0;
    }
}
