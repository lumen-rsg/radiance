using Radiance.Builtins;
using Radiance.Interpreter;

namespace Radiance.Plugins;

/// <summary>
/// Provides a safe API surface for plugins to interact with the Radiance shell.
/// Passed to <see cref="IRadiancePlugin.OnLoad"/> during plugin initialization.
/// </summary>
/// <remarks>
/// Plugins can use this context to:
/// <list type="bullet">
/// <item>Register and unregister custom commands</item>
/// <item>Read and write shell variables</item>
/// <item>Set aliases</item>
/// <item>Access the current working directory and environment</item>
/// </list>
/// </remarks>
public sealed class PluginContext
{
    private readonly ShellContext _shellContext;
    private readonly BuiltinRegistry _registry;
    private readonly List<string> _registeredCommands = new();

    /// <summary>
    /// Creates a new plugin context wrapping the given shell context and builtin registry.
    /// </summary>
    /// <param name="shellContext">The shell execution context.</param>
    /// <param name="registry">The builtin command registry.</param>
    internal PluginContext(ShellContext shellContext, BuiltinRegistry registry)
    {
        _shellContext = shellContext;
        _registry = registry;
    }

    /// <summary>
    /// Gets the shell execution context, providing access to variables,
    /// environment, functions, aliases, and other shell state.
    /// </summary>
    public ShellContext Shell => _shellContext;

    /// <summary>
    /// Registers a custom command with the shell. The command becomes immediately
    /// available as if it were a built-in.
    /// </summary>
    /// <param name="command">The command to register.</param>
    /// <exception cref="ArgumentNullException">Thrown if command is null.</exception>
    public void RegisterCommand(IBuiltinCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _registry.Register(command);
        _registeredCommands.Add(command.Name);
    }

    /// <summary>
    /// Unregisters a previously registered command by name.
    /// Typically used during plugin unload to clean up registered commands.
    /// </summary>
    /// <param name="name">The command name to unregister.</param>
    /// <returns>True if the command was found and removed, false otherwise.</returns>
    public bool UnregisterCommand(string name)
    {
        return _registry.Unregister(name);
    }

    /// <summary>
    /// Gets the list of command names registered by this plugin context.
    /// Used during unload to automatically clean up plugin commands.
    /// </summary>
    internal IReadOnlyList<string> RegisteredCommands => _registeredCommands.AsReadOnly();

    /// <summary>
    /// Unregisters all commands that were registered through this plugin context.
    /// Called automatically during plugin unload.
    /// </summary>
    internal void UnregisterAll()
    {
        foreach (var name in _registeredCommands)
        {
            _registry.Unregister(name);
        }
        _registeredCommands.Clear();
    }
}