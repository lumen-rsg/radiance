using System.Reflection;
using Radiance.Builtins;
using Radiance.Interpreter;
using Radiance.Utils;

namespace Radiance.Plugins;

/// <summary>
/// Manages discovery, loading, and lifecycle of Radiance plugins.
/// Scans <c>~/.radiance/plugins/</c> for .NET DLLs implementing <see cref="IRadiancePlugin"/>.
/// </summary>
public sealed class PluginManager
{
    /// <summary>
    /// Information about a loaded plugin, including its instance and associated context.
    /// </summary>
    private sealed class LoadedPlugin
    {
        public IRadiancePlugin Instance { get; }
        public PluginContext Context { get; }
        public string AssemblyPath { get; }

        public LoadedPlugin(IRadiancePlugin instance, PluginContext context, string assemblyPath)
        {
            Instance = instance;
            Context = context;
            AssemblyPath = assemblyPath;
        }
    }

    private readonly ShellContext _shellContext;
    private readonly BuiltinRegistry _registry;
    private readonly Dictionary<string, LoadedPlugin> _plugins = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The default plugin directory: <c>~/.radiance/plugins/</c>.
    /// </summary>
    public static string DefaultPluginDirectory => Path.Combine(
        Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".radiance",
        "plugins");

    /// <summary>
    /// Creates a new plugin manager.
    /// </summary>
    /// <param name="shellContext">The shell execution context.</param>
    /// <param name="registry">The builtin command registry.</param>
    public PluginManager(ShellContext shellContext, BuiltinRegistry registry)
    {
        _shellContext = shellContext;
        _registry = registry;
    }

    /// <summary>
    /// Gets the number of currently loaded plugins.
    /// </summary>
    public int LoadedCount => _plugins.Count;

    /// <summary>
    /// Discovers and loads all plugins from the default plugin directory.
    /// Creates the directory if it doesn't exist.
    /// </summary>
    public void LoadAll()
    {
        LoadFromDirectory(DefaultPluginDirectory);
    }

    /// <summary>
    /// Discovers and loads all plugins from the specified directory.
    /// </summary>
    /// <param name="directory">The directory to scan for plugin DLLs.</param>
    public void LoadFromDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            return;
        }

        foreach (var dll in Directory.GetFiles(directory, "*.dll"))
        {
            try
            {
                LoadPlugin(dll);
            }
            catch (Exception ex)
            {
                ColorOutput.WriteWarning($"plugin: failed to load '{Path.GetFileName(dll)}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Loads a plugin from a DLL file path.
    /// The DLL must contain at least one type implementing <see cref="IRadiancePlugin"/>.
    /// If multiple implementations are found, all are loaded.
    /// </summary>
    /// <param name="dllPath">The full path to the plugin DLL.</param>
    /// <returns>The number of plugins loaded from the DLL.</returns>
    /// <exception cref="FileNotFoundException">The DLL file doesn't exist.</exception>
    /// <exception cref="InvalidOperationException">No IRadiancePlugin implementation found in the DLL.</exception>
    public int LoadPlugin(string dllPath)
    {
        if (!Path.IsPathRooted(dllPath))
        {
            dllPath = Path.GetFullPath(dllPath);
        }

        if (!File.Exists(dllPath))
        {
            throw new FileNotFoundException($"Plugin DLL not found: {dllPath}");
        }

        var assembly = Assembly.LoadFrom(dllPath);
        var pluginTypes = assembly.GetTypes()
            .Where(t => typeof(IRadiancePlugin).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
            .ToList();

        if (pluginTypes.Count == 0)
        {
            throw new InvalidOperationException(
                $"No {nameof(IRadiancePlugin)} implementation found in '{Path.GetFileName(dllPath)}'");
        }

        var loaded = 0;
        foreach (var type in pluginTypes)
        {
            var plugin = (IRadiancePlugin)Activator.CreateInstance(type)!;
            LoadPluginInstance(plugin, dllPath);
            loaded++;
        }

        return loaded;
    }

    /// <summary>
    /// Loads a single plugin instance, calling its <see cref="IRadiancePlugin.OnLoad"/> method.
    /// </summary>
    private void LoadPluginInstance(IRadiancePlugin plugin, string assemblyPath)
    {
        // Check if a plugin with the same name is already loaded
        if (_plugins.ContainsKey(plugin.Name))
        {
            ColorOutput.WriteWarning($"plugin: '{plugin.Name}' is already loaded, skipping");
            return;
        }

        var context = new PluginContext(_shellContext, _registry);

        try
        {
            plugin.OnLoad(context);
        }
        catch (Exception ex)
        {
            // Rollback any registered commands on failure
            context.UnregisterAll();
            throw new InvalidOperationException(
                $"Plugin '{plugin.Name}' OnLoad failed: {ex.Message}", ex);
        }

        _plugins[plugin.Name] = new LoadedPlugin(plugin, context, assemblyPath);
    }

    /// <summary>
    /// Unloads a plugin by name, calling its <see cref="IRadiancePlugin.OnUnload"/> method
    /// and removing all commands it registered.
    /// </summary>
    /// <param name="name">The plugin name.</param>
    /// <returns>True if the plugin was found and unloaded, false otherwise.</returns>
    public bool UnloadPlugin(string name)
    {
        if (!_plugins.TryGetValue(name, out var loaded))
        {
            return false;
        }

        try
        {
            loaded.Instance.OnUnload();
        }
        catch (Exception ex)
        {
            ColorOutput.WriteWarning($"plugin: error during unload of '{name}': {ex.Message}");
        }

        // Remove all commands registered by this plugin
        loaded.Context.UnregisterAll();
        _plugins.Remove(name);

        return true;
    }

    /// <summary>
    /// Unloads all loaded plugins, calling <see cref="IRadiancePlugin.OnUnload"/> on each.
    /// Called during shell shutdown.
    /// </summary>
    public void UnloadAll()
    {
        foreach (var loaded in _plugins.Values)
        {
            try
            {
                loaded.Instance.OnUnload();
                loaded.Context.UnregisterAll();
            }
            catch (Exception ex)
            {
                ColorOutput.WriteWarning($"plugin: error during unload of '{loaded.Instance.Name}': {ex.Message}");
            }
        }

        _plugins.Clear();
    }

    /// <summary>
    /// Checks if a plugin with the given name is currently loaded.
    /// </summary>
    /// <param name="name">The plugin name.</param>
    /// <returns>True if the plugin is loaded.</returns>
    public bool IsLoaded(string name) => _plugins.ContainsKey(name);

    /// <summary>
    /// Gets information about all loaded plugins.
    /// Returns a list of (Name, Version, Description) tuples.
    /// </summary>
    public IReadOnlyList<(string Name, string Version, string Description)> GetLoadedPlugins()
    {
        return _plugins.Values
            .Select(p => (p.Instance.Name, p.Instance.Version, p.Instance.Description))
            .ToList()
            .AsReadOnly();
    }
}