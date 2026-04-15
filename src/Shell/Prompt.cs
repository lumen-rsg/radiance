using System.Text;
using Radiance.Interpreter;

namespace Radiance.Shell;

/// <summary>
/// Renders the interactive shell prompt in the format: user@host:directory$
/// Supports colorized output and custom PS1-style prompts.
/// </summary>
public static class Prompt
{
    // ANSI color codes
    private const string Reset = "\x1b[0m";
    private const string Green = "\x1b[32m";
    private const string Cyan = "\x1b[36m";
    private const string Bold = "\x1b[1m";

    /// <summary>
    /// Renders the shell prompt string.
    /// </summary>
    /// <param name="context">The current execution context.</param>
    /// <returns>The formatted prompt string (may contain ANSI escape codes).</returns>
    public static string Render(ShellContext context)
    {
        var user = Environment.GetEnvironmentVariable("USER") ?? "user";
        var host = Environment.MachineName ?? "localhost";
        var dir = GetDisplayDirectory(context);

        var sb = new StringBuilder();
        sb.Append(Green).Append(Bold).Append(user).Append('@').Append(host).Append(Reset);
        sb.Append(':');
        sb.Append(Cyan).Append(dir).Append(Reset);
        sb.Append("$ ");

        return sb.ToString();
    }

    /// <summary>
    /// Gets a display-friendly version of the current directory,
    /// replacing the home directory prefix with ~.
    /// </summary>
    private static string GetDisplayDirectory(ShellContext context)
    {
        var home = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;
        var dir = context.CurrentDirectory;

        if (!string.IsNullOrEmpty(home) && dir.StartsWith(home))
        {
            return "~" + dir[home.Length..];
        }

        return dir;
    }

    /// <summary>
    /// Gets the current git branch name, or null if not in a git repo.
    /// </summary>
    public static string? GetGitBranch()
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse --abbrev-ref HEAD",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null) return null;
            var output = process.StandardOutput.ReadLine()?.Trim();
            process.WaitForExit(1000);
            return process.ExitCode == 0 && !string.IsNullOrEmpty(output) ? output : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Checks if the current git repo has uncommitted changes.
    /// </summary>
    public static bool IsGitDirty()
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "status --porcelain",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null) return false;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(1000);
            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output);
        }
        catch
        {
            return false;
        }
    }
}
