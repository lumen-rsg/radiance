using Radiance.Builtins;
using Radiance.Interpreter;
using Radiance.Utils;

namespace Radiance.Plugins;

/// <summary>
/// Built-in <c>plugin</c> command — manages Radiance plugins.
/// Supports listing, loading, and unloading plugins at runtime.
/// </summary>
/// <remarks>
/// Usage:
/// <list type="bullet">
/// <item><c>plugin list</c> — show all loaded plugins</item>
/// <item><c>plugin load <path></c> — load a plugin from a DLL path</item>
/// <item><c>plugin unload <name></c> — unload a plugin by name</item>
/// </list>
/// </remarks>
public sealed class PluginCommand : IBuiltinCommand
{
    public string Name => "plugin";

    /// <summary>
    /// The plugin manager instance, set during shell initialization.
    /// </summary>
    public PluginManager? Manager { get; set; }

    public int Execute(string[] args, ShellContext context)
    {
        if (Manager is null)
        {
            ColorOutput.WriteError("plugin: plugin system not initialized");
            return 1;
        }

        if (args.Length < 2)
        {
            PrintUsage();
            return 1;
        }

        var subcommand = args[1];

        switch (subcommand)
        {
            case "list":
                return ExecuteList();
            case "load":
                return ExecuteLoad(args);
            case "unload":
                return ExecuteUnload(args);
            case "help":
                PrintUsage();
                return 0;
            default:
                ColorOutput.WriteError($"plugin: unknown subcommand '{subcommand}'");
                return 1;
        }
    }

    /// <summary>
    /// Handles <c>plugin list</c> — displays all loaded plugins.
    /// </summary>
    private int ExecuteList()
    {
        var plugins = Manager!.GetLoadedPlugins();

        if (plugins.Count == 0)
        {
            Console.WriteLine("No plugins loaded.");
            Console.WriteLine($"Plugin directory: {PluginManager.DefaultPluginDirectory}");
            return 0;
        }

        Console.WriteLine($"\x1b[1;33mLoaded Plugins ({plugins.Count}):\x1b[0m");
        Console.WriteLine(new string('─', 50));

        foreach (var (name, version, description) in plugins)
        {
            Console.WriteLine($"  \x1b[1;36m{name}\x1b[0m \x1b[37mv{version}\x1b[0m");
            if (!string.IsNullOrEmpty(description))
            {
                Console.WriteLine($"    {description}");
            }
        }

        Console.WriteLine();
        return 0;
    }

    /// <summary>
    /// Handles <c>plugin load <path></c> — loads a plugin from a DLL.
    /// </summary>
    private int ExecuteLoad(string[] args)
    {
        if (args.Length < 3)
        {
            ColorOutput.WriteError("plugin load: missing DLL path argument");
            return 1;
        }

        var path = args[2];

        // Resolve relative paths against CWD
        if (!Path.IsPathRooted(path))
        {
            path = Path.GetFullPath(path);
        }

        try
        {
            var count = Manager!.LoadPlugin(path);
            Console.WriteLine($"Loaded {count} plugin(s) from '{Path.GetFileName(path)}'");
            return 0;
        }
        catch (Exception ex)
        {
            ColorOutput.WriteError($"plugin load: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Handles <c>plugin unload <name></c> — unloads a plugin by name.
    /// </summary>
    private int ExecuteUnload(string[] args)
    {
        if (args.Length < 3)
        {
            ColorOutput.WriteError("plugin unload: missing plugin name argument");
            return 1;
        }

        var name = args[2];

        if (Manager!.UnloadPlugin(name))
        {
            Console.WriteLine($"Plugin '{name}' unloaded successfully");
            return 0;
        }

        ColorOutput.WriteError($"plugin unload: '{name}' is not loaded");
        return 1;
    }

    /// <summary>
    /// Prints usage information for the plugin command.
    /// </summary>
    private static void PrintUsage()
    {
        Console.WriteLine("""
            plugin — manage Radiance shell plugins

            Usage:
              plugin list              List all loaded plugins
              plugin load <path>       Load a plugin from a DLL file
              plugin unload <name>     Unload a plugin by name
              plugin help              Show this help message

            Plugins are loaded automatically from ~/.radiance/plugins/ on startup.
            """);
    }
}