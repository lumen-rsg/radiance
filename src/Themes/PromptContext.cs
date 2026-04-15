using System;
using System.Collections.Generic;

namespace Radiance.Themes;

/// <summary>
/// Provides all data a theme needs to render a prompt.
/// </summary>
public sealed class PromptContext
{
    public string User { get; set; } = Environment.UserName;
    public string Host { get; set; } = Environment.MachineName;
    public string HomeDir { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string Cwd { get; set; } = Directory.GetCurrentDirectory();
    public int LastExitCode { get; set; }
    public bool IsRoot { get; set; }
    public string? GitBranch { get; set; }
    public bool GitDirty { get; set; }
    public int JobCount { get; set; }
    public DateTime Now { get; set; } = DateTime.Now;
    public string ShellName { get; set; } = "radiance";

    /// <summary>
    /// Access to shell environment variables for custom prompt data.
    /// </summary>
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; set; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Returns CWD with home directory replaced by ~ if applicable.
    /// </summary>
    public string TildeCwd =>
        Cwd.StartsWith(HomeDir) ? "~" + Cwd[HomeDir.Length..] : Cwd;

    /// <summary>
    /// Returns just the last directory component of CWD.
    /// </summary>
    public string ShortCwd => Path.GetFileName(Cwd) ?? Cwd;

    /// <summary>
    /// Time formatted as HH:MM:SS.
    /// </summary>
    public string Time => Now.ToString("HH:mm:ss");

    /// <summary>
    /// Date formatted asddd MMM DD.
    /// </summary>
    public string Date => Now.ToString("ddd MMM dd");

    /// <summary>
    /// The prompt character: # if root, $ otherwise (or custom via last exit code).
    /// </summary>
    public string PromptChar => LastExitCode == 0 ? "$" : "$";
}