using System;
using System.Linq;
using Radiance.Interpreter;
using Radiance.Themes;

namespace Radiance.Builtins;

/// <summary>
/// Built-in command for managing themes.
/// Usage: theme [list|set <name>|current|info <name>|path]
/// </summary>
public sealed class ThemeCommand : IBuiltinCommand
{
    public string Name => "theme";

    private readonly ThemeManager _themeManager;

    public ThemeCommand(ThemeManager themeManager)
    {
        _themeManager = themeManager;
    }

    public int Execute(string[] args, ShellContext ctx)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        var subcommand = args[0].ToLowerInvariant();

        return subcommand switch
        {
            "list" or "ls" => ListThemes(),
            "set" => SetTheme(args),
            "current" or "show" => ShowCurrent(),
            "info" => ShowInfo(args),
            "path" => ShowPath(),
            "help" => PrintHelp(),
            _ => UnknownSubcommand(subcommand)
        };
    }

    private int ListThemes()
    {
        var current = _themeManager.ActiveTheme.Name;
        Console.WriteLine("Available themes:");
        Console.WriteLine();

        foreach (var theme in _themeManager.AllThemes.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            var marker = theme.Name.Equals(current, StringComparison.OrdinalIgnoreCase) ? " *" : "";
            var author = string.IsNullOrEmpty(theme.Author) || theme.Author == "Radiance"
                ? ""
                : $" by {theme.Author}";
            Console.WriteLine($"  {theme.Name,-16} {theme.Description}{author}{marker}");
        }

        Console.WriteLine();
        Console.WriteLine($"  ({_themeManager.Count} themes, current: {current})");
        return 0;
    }

    private int SetTheme(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("theme set: theme name required");
            Console.Error.WriteLine("Usage: theme set <name>");
            Console.Error.WriteLine("Use 'theme list' to see available themes.");
            return 1;
        }

        var name = args[1];
        if (!_themeManager.SetTheme(name))
        {
            Console.Error.WriteLine($"theme set: unknown theme '{name}'");
            Console.Error.WriteLine("Use 'theme list' to see available themes.");
            return 1;
        }

        _themeManager.SaveConfig();
        Console.WriteLine($"Theme set to '{_themeManager.ActiveTheme.Name}'");
        return 0;
    }

    private int ShowCurrent()
    {
        var theme = _themeManager.ActiveTheme;
        Console.WriteLine($"Current theme: {theme.Name}");
        Console.WriteLine($"  Description: {theme.Description}");
        Console.WriteLine($"  Author:      {theme.Author}");
        return 0;
    }

    private int ShowInfo(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("theme info: theme name required");
            Console.Error.WriteLine("Usage: theme info <name>");
            return 1;
        }

        var theme = _themeManager.GetTheme(args[1]);
        if (theme == null)
        {
            Console.Error.WriteLine($"theme info: unknown theme '{args[1]}'");
            return 1;
        }

        Console.WriteLine($"Name:        {theme.Name}");
        Console.WriteLine($"Description: {theme.Description}");
        Console.WriteLine($"Author:      {theme.Author}");

        // Show a preview of the prompt
        var previewCtx = new PromptContext
        {
            User = "user",
            Host = "host",
            Cwd = "/home/user/projects/radiance",
            HomeDir = "/home/user",
            LastExitCode = 0,
            GitBranch = "main",
            GitDirty = false,
            JobCount = 0
        };

        Console.WriteLine();
        Console.WriteLine("Preview:");
        Console.Write("  ");
        Console.WriteLine(theme.RenderPrompt(previewCtx));

        var rprompt = theme.RenderRightPrompt(previewCtx);
        if (!string.IsNullOrEmpty(rprompt))
        {
            Console.WriteLine($"  RPROMPT: {rprompt}");
        }

        return 0;
    }

    private int ShowPath()
    {
        Console.WriteLine($"Custom themes directory: {ThemeManager.ThemesDirectory}");
        Console.WriteLine($"Configuration file:      {ThemeManager.ConfigPath}");

        if (!System.IO.Directory.Exists(ThemeManager.ThemesDirectory))
        {
            Console.WriteLine();
            Console.WriteLine("Custom themes directory does not exist yet.");
            Console.WriteLine("Create it and add .json theme files to load custom themes:");
            Console.WriteLine($"  mkdir -p {ThemeManager.ThemesDirectory}");
        }

        return 0;
    }

    private int PrintHelp()
    {
        Console.WriteLine("theme - Manage Radiance shell themes");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  theme list              List available themes");
        Console.WriteLine("  theme set <name>        Switch to a theme");
        Console.WriteLine("  theme current           Show current theme");
        Console.WriteLine("  theme info <name>       Show theme details and preview");
        Console.WriteLine("  theme path              Show custom themes directory");
        Console.WriteLine("  theme help              Show this help message");
        return 0;
    }

    private int UnknownSubcommand(string sub)
    {
        Console.Error.WriteLine($"theme: unknown subcommand '{sub}'");
        Console.Error.WriteLine("Use 'theme help' for usage information.");
        return 1;
    }
}