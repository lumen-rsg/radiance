namespace Radiance.Plugins;

/// <summary>
/// Interface that all Radiance plugins must implement.
/// Plugins are .NET DLLs placed in <c>~/.radiance/plugins/</c> that extend
/// the shell with custom commands and behavior.
/// </summary>
/// <remarks>
/// To create a plugin:
/// <list type="number">
/// <item>Create a class library project targeting .NET 10.0</item>
/// <item>Reference the Radiance project (or copy the interfaces)</item>
/// <item>Implement <see cref="IRadiancePlugin"/></item>
/// <item>Build and place the DLL in <c>~/.radiance/plugins/</c></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// public class HelloPlugin : IRadiancePlugin
/// {
///     public string Name => "hello";
///     public string Version => "1.0.0";
///     public string Description => "Adds a 'hello' greeting command";
///
///     public void OnLoad(PluginContext context)
///     {
///         context.RegisterCommand(new HelloCommand());
///     }
///
///     public void OnUnload() { }
/// }
/// </code>
/// </example>
public interface IRadiancePlugin
{
    /// <summary>
    /// The unique name of the plugin. Used for identification and management.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The version of the plugin (e.g., "1.0.0").
    /// </summary>
    string Version { get; }

    /// <summary>
    /// A short human-readable description of what the plugin does.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Called when the plugin is loaded. Use the provided <see cref="PluginContext"/>
    /// to register commands, access shell state, and set up the plugin.
    /// </summary>
    /// <param name="context">The plugin context providing access to the shell.</param>
    void OnLoad(PluginContext context);

    /// <summary>
    /// Called when the plugin is being unloaded (e.g., via <c>plugin unload</c> or shell exit).
    /// Clean up any resources here.
    /// </summary>
    void OnUnload();
}
