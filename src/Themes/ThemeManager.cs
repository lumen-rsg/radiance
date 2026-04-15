using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Radiance.Themes;

/// <summary>
/// Manages theme loading, registration, and switching.
/// </summary>
public sealed class ThemeManager
{
    private readonly Dictionary<string, ITheme> _themes = new(StringComparer.OrdinalIgnoreCase);
    private ITheme _activeTheme;

    /// <summary>
    /// Directory where user custom JSON themes are stored.
    /// </summary>
    public static string ThemesDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".radiance", "themes"
    );

    /// <summary>
    /// Path to the configuration file.
    /// </summary>
    public static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".radiance", "config.json"
    );

    public ThemeManager()
    {
        // Default theme is always available
        _activeTheme = new Builtins.DefaultTheme();
        RegisterTheme(_activeTheme);
    }

    /// <summary>
    /// The currently active theme.
    /// </summary>
    public ITheme ActiveTheme => _activeTheme;

    /// <summary>
    /// All registered theme names.
    /// </summary>
    public IEnumerable<string> ThemeNames => _themes.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// All registered themes.
    /// </summary>
    public IEnumerable<ITheme> AllThemes => _themes.Values;

    /// <summary>
    /// Number of registered themes.
    /// </summary>
    public int Count => _themes.Count;

    /// <summary>
    /// Register a theme.
    /// </summary>
    public void RegisterTheme(ITheme theme)
    {
        _themes[theme.Name] = theme;
    }

    /// <summary>
    /// Get a theme by name. Returns null if not found.
    /// </summary>
    public ITheme? GetTheme(string name)
    {
        return _themes.TryGetValue(name, out var theme) ? theme : null;
    }

    /// <summary>
    /// Check if a theme with the given name is registered.
    /// </summary>
    public bool HasTheme(string name) => _themes.ContainsKey(name);

    /// <summary>
    /// Set the active theme by name. Returns true if successful.
    /// </summary>
    public bool SetTheme(string name)
    {
        if (!_themes.TryGetValue(name, out var theme)) return false;
        _activeTheme = theme;
        return true;
    }

    /// <summary>
    /// Initialize all built-in and custom themes.
    /// </summary>
    public void Initialize()
    {
        // Register built-in themes
        RegisterTheme(new Builtins.DefaultTheme());
        RegisterTheme(new Builtins.MinimalTheme());
        RegisterTheme(new Builtins.PowerlineTheme());
        RegisterTheme(new Builtins.RainbowTheme());
        RegisterTheme(new Builtins.DarkTheme());
        RegisterTheme(new Builtins.LightTheme());

        // Discover and load custom JSON themes
        LoadCustomThemes();

        // Load saved theme preference
        LoadConfig();
    }

    /// <summary>
    /// Discover and load JSON themes from ~/.radiance/themes/
    /// </summary>
    public void LoadCustomThemes()
    {
        if (!Directory.Exists(ThemesDirectory)) return;

        foreach (var file in Directory.GetFiles(ThemesDirectory, "*.json"))
        {
            var theme = JsonTheme.LoadFromFile(file);
            if (theme != null)
            {
                RegisterTheme(theme);
            }
        }
    }

    /// <summary>
    /// Save the current theme preference to config file.
    /// </summary>
    public void SaveConfig()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var config = new RadianceConfig { Theme = _activeTheme.Name };
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // Silently fail — theme preference is not critical
        }
    }

    /// <summary>
    /// Load theme preference from config file.
    /// </summary>
    public void LoadConfig()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return;

            var json = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<RadianceConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (config?.Theme != null && _themes.ContainsKey(config.Theme))
            {
                _activeTheme = _themes[config.Theme];
            }
        }
        catch
        {
            // Silently fail — fall back to default theme
        }
    }

    /// <summary>
    /// Render the prompt using the active theme.
    /// </summary>
    public string RenderPrompt(PromptContext ctx) => _activeTheme.RenderPrompt(ctx);

    /// <summary>
    /// Render the right prompt using the active theme.
    /// </summary>
    public string RenderRightPrompt(PromptContext ctx) => _activeTheme.RenderRightPrompt(ctx);
}

internal sealed class RadianceConfig
{
    public string Theme { get; set; } = "default";
}