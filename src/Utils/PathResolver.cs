namespace Radiance.Utils;

/// <summary>
/// Resolves command names to full executable paths by searching the system PATH.
/// Results are cached with a short TTL to avoid repeated filesystem scans.
/// </summary>
public static class PathResolver
{
    private static readonly string[] ExecutableExtensions =
        OperatingSystem.IsWindows()
            ? [".exe", ".cmd", ".bat", ".ps1", ""]
            : [""];

    /// <summary>
    /// Cache entry storing the resolved path and the timestamp when it was cached.
    /// </summary>
    private record struct CacheEntry(string? Path, long TimestampTicks);

    /// <summary>
    /// Cache TTL in ticks (5 seconds).
    /// </summary>
    private static readonly long CacheTtlTicks = TimeSpan.FromSeconds(5).Ticks;

    private static readonly Dictionary<string, CacheEntry> Cache = new(StringComparer.Ordinal);
    private static long _lastCleanupTicks;
    private static readonly object CacheLock = new();

    /// <summary>
    /// Resolves a command name to its full path by searching PATH directories.
    /// Results are cached for 5 seconds to avoid repeated filesystem scans.
    /// </summary>
    /// <param name="commandName">The command name to resolve.</param>
    /// <returns>The full path to the executable, or null if not found.</returns>
    public static string? Resolve(string commandName)
    {
        // If it's already an absolute or relative path, check it directly (no caching)
        if (commandName.Contains('/') || commandName.Contains('\\'))
        {
            return File.Exists(commandName) ? Path.GetFullPath(commandName) : null;
        }

        var now = Environment.TickCount64;

        lock (CacheLock)
        {
            // Periodic cleanup of expired entries (every ~30 seconds)
            if (now - _lastCleanupTicks > 30_000)
            {
                _lastCleanupTicks = now;
                var expiredKeys = new List<string>();
                foreach (var kvp in Cache)
                {
                    if (now - kvp.Value.TimestampTicks > 5_000)
                        expiredKeys.Add(kvp.Key);
                }
                foreach (var key in expiredKeys)
                    Cache.Remove(key);
            }

            // Check cache
            if (Cache.TryGetValue(commandName, out var entry))
            {
                if (now - entry.TimestampTicks <= 5_000)
                    return entry.Path;
                Cache.Remove(commandName);
            }
        }

        // Resolve outside lock (filesystem access)
        var resolved = ResolveUncached(commandName);

        lock (CacheLock)
        {
            Cache[commandName] = new CacheEntry(resolved, now);
        }

        return resolved;
    }

    /// <summary>
    /// Performs the actual PATH resolution without caching.
    /// </summary>
    private static string? ResolveUncached(string commandName)
    {
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
    /// Result of a PATH resolution that includes executability information.
    /// </summary>
    /// <param name="Path">The resolved path, or null if not found.</param>
    /// <param name="FoundButNotExecutable">True if the file exists but is not executable.</param>
    public record struct ResolveResult(string? Path, bool FoundButNotExecutable);

    /// <summary>
    /// Resolves a command name with additional information about whether the file
    /// exists but is not executable (for "Permission denied" error reporting).
    /// </summary>
    /// <param name="commandName">The command name to resolve.</param>
    /// <returns>A <see cref="ResolveResult"/> with the path and executability info.</returns>
    public static ResolveResult? ResolveWithExecutability(string commandName)
    {
        // If it's a path, check directly
        if (commandName.Contains('/') || commandName.Contains('\\'))
        {
            if (File.Exists(commandName))
            {
                return IsExecutable(commandName)
                    ? new ResolveResult(commandName, false)
                    : new ResolveResult(null, true);
            }
            return null;
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
                        return IsExecutable(fullPath)
                            ? new ResolveResult(fullPath, false)
                            : new ResolveResult(null, true);
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
    /// Checks if a file is executable (has execute permission on Unix, or is .exe/.cmd/.bat/.ps1 on Windows).
    /// </summary>
    private static bool IsExecutable(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                return ext is ".exe" or ".cmd" or ".bat" or ".ps1" or "";
            }

            // On Unix: check if the file has any execute bit set
            using var stream = File.OpenRead(path);
            // File.Exists already confirmed it's a regular file
            // Check Unix permissions via mono/runtime
            var fi = new FileInfo(path);
            // On .NET 10+ Unix, we can check the Unix file mode
            return (fi.UnixFileMode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if a command exists in the PATH.
    /// </summary>
    /// <param name="commandName">The command name to check.</param>
    /// <returns>True if the command is found.</returns>
    public static bool Exists(string commandName) => Resolve(commandName) is not null;

    /// <summary>
    /// Invalidates the entire PATH cache. Called when PATH changes.
    /// </summary>
    public static void InvalidateCache()
    {
        lock (CacheLock)
        {
            Cache.Clear();
        }
    }
}