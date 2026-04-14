namespace Radiance.Utils;

/// <summary>
/// Resolves command names to full executable paths by searching the system PATH.
/// </summary>
public static class PathResolver
{
    private static readonly string[] ExecutableExtensions =
        OperatingSystem.IsWindows()
            ? [".exe", ".cmd", ".bat", ".ps1", ""]
            : [""];

    /// <summary>
    /// Resolves a command name to its full path by searching PATH directories.
    /// </summary>
    /// <param name="commandName">The command name to resolve.</param>
    /// <returns>The full path to the executable, or null if not found.</returns>
    public static string? Resolve(string commandName)
    {
        // If it's already an absolute or relative path, check it directly
        if (commandName.Contains('/') || commandName.Contains('\\'))
        {
            return File.Exists(commandName) ? Path.GetFullPath(commandName) : null;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var separator = OperatingSystem.IsWindows() ? ';' : ':';

        foreach (var dir in pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                foreach (var ext in ExecutableExtensions)
                {
                    var fullPath = Path.Combine(dir, commandName + ext);
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
            }
            catch (Exception)
            {
                // Skip inaccessible directories
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if a command exists in the PATH.
    /// </summary>
    /// <param name="commandName">The command name to check.</param>
    /// <returns>True if the command is found.</returns>
    public static bool Exists(string commandName) => Resolve(commandName) is not null;
}