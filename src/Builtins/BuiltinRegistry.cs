using Radiance.Builtins;
using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Registry that holds all available built-in commands and dispatches execution.
/// </summary>
public sealed class BuiltinRegistry
{
    private readonly Dictionary<string, IBuiltinCommand> _commands = new();

    /// <summary>
    /// Registers a built-in command.
    /// </summary>
    /// <param name="command">The command to register.</param>
    public void Register(IBuiltinCommand command)
    {
        _commands[command.Name] = command;
    }

    /// <summary>
    /// Checks if a command name is a registered built-in.
    /// </summary>
    /// <param name="name">The command name.</param>
    /// <returns>True if the command is a built-in.</returns>
    public bool IsBuiltin(string name) => _commands.ContainsKey(name);

    /// <summary>
    /// Tries to execute a built-in command.
    /// </summary>
    /// <param name="name">The command name.</param>
    /// <param name="args">The arguments (including command name as args[0]).</param>
    /// <param name="context">The execution context.</param>
    /// <param name="exitCode">The resulting exit code.</param>
    /// <returns>True if the command was found and executed, false otherwise.</returns>
    public bool TryExecute(string name, string[] args, ShellContext context, out int exitCode)
    {
        if (_commands.TryGetValue(name, out var command))
        {
            exitCode = command.Execute(args, context);
            return true;
        }

        exitCode = 1;
        return false;
    }

    /// <summary>
    /// Gets all registered built-in command names.
    /// </summary>
    public IEnumerable<string> CommandNames => _commands.Keys;

    /// <summary>
    /// Gets a registered command by name, or null if not found.
    /// </summary>
    /// <param name="name">The command name.</param>
    /// <returns>The command instance, or null.</returns>
    public IBuiltinCommand? TryGetCommand(string name) =>
        _commands.TryGetValue(name, out var cmd) ? cmd : null;

    /// <summary>
    /// Unregisters a built-in command by name.
    /// Used by the plugin system to remove commands when a plugin is unloaded.
    /// </summary>
    /// <param name="name">The command name to unregister.</param>
    /// <returns>True if the command was found and removed, false otherwise.</returns>
    public bool Unregister(string name) => _commands.Remove(name);

    /// <summary>
    /// Creates a new registry pre-loaded with all standard built-in commands.
    /// </summary>
    /// <returns>A fully populated <see cref="BuiltinRegistry"/>.</returns>
    public static BuiltinRegistry CreateDefault()
    {
        var registry = new BuiltinRegistry();
        registry.Register(new EchoCommand());
        registry.Register(new CdCommand());
        registry.Register(new PwdCommand());
        registry.Register(new ExitCommand());
        registry.Register(new ExportCommand());
        registry.Register(new UnsetCommand());
        var typeCmd = new TypeCommand();
        typeCmd.SetRegistry(registry);
        registry.Register(typeCmd);
        registry.Register(new SetCommand());
        registry.Register(new EnvCommand());
        registry.Register(new TrueCommand());
        registry.Register(new FalseCommand());
        registry.Register(new ReturnCommand());
        registry.Register(new LocalCommand());
        registry.Register(new AliasCommand());
        registry.Register(new UnaliasCommand());
        registry.Register(new JobsCommand());
        registry.Register(new FgCommand());
        registry.Register(new HistoryCommand());
        registry.Register(new BreakCommand());
        registry.Register(new ContinueCommand());
        registry.Register(new SourceCommand());
        registry.Register(SourceCommand.CreateDotAlias());
        registry.Register(new ReadCommand());
        return registry;
    }
}