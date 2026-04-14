using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Interface for all built-in shell commands.
/// </summary>
public interface IBuiltinCommand
{
    /// <summary>
    /// The name of the built-in command (e.g., "cd", "echo", "exit").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Executes the built-in command with the given arguments.
    /// </summary>
    /// <param name="args">The arguments passed to the command (including the command name as args[0]).</param>
    /// <param name="context">The current execution context.</param>
    /// <returns>The exit code (0 for success, non-zero for failure).</returns>
    int Execute(string[] args, ShellContext context);
}