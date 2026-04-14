using System.Text;
using Radiance.Interpreter;

namespace Radiance.Expansion;

/// <summary>
/// Performs tilde (<c>~</c>) expansion on shell words.
/// <para>
/// BASH tilde expansion rules:
/// <list type="bullet">
/// <item><c>~</c> → value of $HOME</item>
/// <item><c>~/path</c> → $HOME/path</item>
/// <item><c>~user</c> → home directory of <c>user</c></item>
/// </list>
/// Tilde expansion only applies to unquoted words at the start of a word.
/// </para>
/// </summary>
public sealed class TildeExpander
{
    /// <summary>
    /// Expands tildes in the given text. Only applies when the text starts with <c>~</c>
    /// and is not quoted.
    /// </summary>
    /// <param name="text">The text to expand.</param>
    /// <param name="isQuoted">Whether the text is quoted (suppresses tilde expansion).</param>
    /// <param name="context">The shell context (for looking up HOME).</param>
    /// <returns>The expanded text, or the original if no expansion applied.</returns>
    public static string Expand(string text, bool isQuoted, ShellContext context)
    {
        if (isQuoted || string.IsNullOrEmpty(text) || text[0] != '~')
            return text;

        // Find the end of the tilde prefix (up to '/' or end of string)
        var slashIdx = text.IndexOf('/');
        var tildePrefix = slashIdx >= 0 ? text[1..slashIdx] : text[1..];

        if (string.IsNullOrEmpty(tildePrefix))
        {
            // Plain ~ → $HOME
            var home = context.GetVariable("HOME");
            if (string.IsNullOrEmpty(home))
                home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (slashIdx >= 0)
                return home + text[slashIdx..];
            return home;
        }

        // ~user → look up user's home directory
        var userHome = GetUserHome(tildePrefix);
        if (userHome is not null)
        {
            if (slashIdx >= 0)
                return userHome + text[slashIdx..];
            return userHome;
        }

        // Couldn't resolve — return original
        return text;
    }

    /// <summary>
    /// Attempts to resolve a user's home directory by name.
    /// Uses OS-level directory services on macOS/Linux.
    /// </summary>
    private static string? GetUserHome(string username)
    {
        try
        {
            // On Unix-like systems, try to look up the home directory
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
            {
                // Use getent or check /etc/passwd equivalent
                var homeDir = GetUserHomeUnix(username);
                if (homeDir is not null)
                    return homeDir;
            }

            // Fallback: check if the username matches the current user
            var currentUser = Environment.UserName;
            if (string.Equals(username, currentUser, StringComparison.OrdinalIgnoreCase))
            {
                return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
        }
        catch
        {
            // Ignore errors — return null
        }

        return null;
    }

    /// <summary>
    /// Resolves a user's home directory on Unix-like systems using the system's user database.
    /// </summary>
    private static string? GetUserHomeUnix(string username)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dscl",
                Arguments = $". -read /Users/{username} NFSHomeDirectory",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null)
                return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0 && output.StartsWith("NFSHomeDirectory:"))
            {
                var path = output["NFSHomeDirectory:".Length..].Trim();
                if (!string.IsNullOrEmpty(path))
                    return path;
            }
        }
        catch
        {
            // Ignore — fallback
        }

        return null;
    }
}