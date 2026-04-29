namespace Radiance.Shell;

/// <summary>
/// Manages command history with navigation (up/down arrows) and
/// persistence across sessions via a file on disk.
/// </summary>
public sealed class History
{
    private readonly List<string> _entries = new();
    private int _navigationIndex = -1;

    /// <summary>
    /// The maximum number of history entries to keep in memory.
    /// </summary>
    public int MaxEntries { get; set; } = 1000;

    /// <summary>
    /// The path to the history file for persistent storage.
    /// Defaults to <c>~/.radiance_history</c>.
    /// </summary>
    public string FilePath { get; set; } = Path.Combine(
        Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".radiance_history");

    /// <summary>
    /// Gets the current number of history entries.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Adds a command to the history. Duplicates of the most recent entry are skipped.
    /// </summary>
    /// <param name="command">The command string to add.</param>
    public void Add(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;

        // Don't add if same as last entry
        if (_entries.Count > 0 && _entries[^1] == command)
            return;

        if (_entries.Count >= MaxEntries)
            _entries.RemoveAt(0);

        _entries.Add(command);
        _navigationIndex = _entries.Count; // Reset navigation to end
    }

    /// <summary>
    /// Navigates up (older) in history and returns the entry, or null if at the beginning.
    /// </summary>
    public string? NavigateUp()
    {
        if (_entries.Count == 0 || _navigationIndex <= 0)
            return null;

        _navigationIndex--;
        return _entries[_navigationIndex];
    }

    /// <summary>
    /// Navigates down (newer) in history and returns the entry, or null if at the end.
    /// </summary>
    public string? NavigateDown()
    {
        if (_navigationIndex >= _entries.Count - 1)
        {
            _navigationIndex = _entries.Count;
            return null;
        }

        _navigationIndex++;
        return _entries[_navigationIndex];
    }

    /// <summary>
    /// Resets navigation position to the end of history.
    /// </summary>
    public void ResetNavigation()
    {
        _navigationIndex = _entries.Count;
    }

    /// <summary>
    /// Searches history entries that contain the given query string.
    /// Returns matches in reverse chronological order (newest first).
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <returns>Matching history entries, newest first.</returns>
    public IEnumerable<string> SearchEntries(string query)
    {
        if (string.IsNullOrEmpty(query))
            return Enumerable.Empty<string>();

        var results = new List<string>();
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].Contains(query, StringComparison.Ordinal))
                results.Add(_entries[i]);
        }
        return results;
    }

    /// <summary>
    /// Gets the most recent history entry that starts with the given prefix.
    /// </summary>
    public string? GetEntryByPrefix(string prefix)
    {
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].StartsWith(prefix, StringComparison.Ordinal))
                return _entries[i];
        }
        return null;
    }

    /// <summary>
    /// Gets the most recent history entry that contains the given substring.
    /// </summary>
    public string? GetEntryBySubstring(string substring)
    {
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].Contains(substring, StringComparison.Ordinal))
                return _entries[i];
        }
        return null;
    }

    /// <summary>
    /// Gets the last argument of the most recent history entry.
    /// Returns empty string if no history.
    /// </summary>
    public string GetLastArg()
    {
        if (_entries.Count == 0)
            return string.Empty;

        var parts = SplitIntoArgs(_entries[^1]);
        return parts.Count > 0 ? parts[^1] : string.Empty;
    }

    /// <summary>
    /// Gets the first argument of the most recent history entry.
    /// Returns empty string if no history.
    /// </summary>
    public string GetFirstArg()
    {
        if (_entries.Count == 0)
            return string.Empty;

        var parts = SplitIntoArgs(_entries[^1]);
        return parts.Count > 1 ? parts[1] : string.Empty;
    }

    /// <summary>
    /// Gets all arguments (excluding command name) of the most recent history entry.
    /// </summary>
    public List<string> GetAllArgs()
    {
        if (_entries.Count == 0)
            return [];

        var parts = SplitIntoArgs(_entries[^1]);
        return parts.Count > 1 ? parts[1..] : [];
    }

    /// <summary>
    /// Splits a command string into arguments, respecting quoted strings.
    /// </summary>
    private static List<string> SplitIntoArgs(string command)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        var inSingleQuote = false;
        var inDoubleQuote = false;

        foreach (var c in command)
        {
            if (c == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (c == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inSingleQuote && !inDoubleQuote)
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
            args.Add(current.ToString());

        return args;
    }

    /// <summary>
    /// Returns all history entries in order.
    /// </summary>
    public IEnumerable<string> GetAll() => _entries;

    /// <summary>
    /// Clears all history entries.
    /// </summary>
    public void Clear()
    {
        _entries.Clear();
        _navigationIndex = -1;
    }

    /// <summary>
    /// Deletes a history entry by 1-based index.
    /// </summary>
    /// <param name="offset">1-based index of the entry to delete.</param>
    /// <returns>True if the entry was deleted, false if the offset was out of range.</returns>
    public bool Delete(int offset)
    {
        if (offset < 1 || offset > _entries.Count)
            return false;

        _entries.RemoveAt(offset - 1);
        _navigationIndex = _entries.Count;
        return true;
    }

    /// <summary>
    /// Gets the entry at the given 1-based index, or null if out of range.
    /// </summary>
    /// <param name="index">1-based history index.</param>
    /// <returns>The entry, or null.</returns>
    public string? GetEntry(int index)
    {
        if (index < 1 || index > _entries.Count)
            return null;
        return _entries[index - 1];
    }

    /// <summary>
    /// Loads history entries from the persistent history file.
    /// Existing in-memory entries are replaced.
    /// </summary>
    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return;

            var lines = File.ReadAllLines(FilePath);
            _entries.Clear();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Handle legacy timestamp lines (lines starting with #)
                if (line.StartsWith('#'))
                    continue;

                _entries.Add(line);

                if (_entries.Count >= MaxEntries)
                    break;
            }

            _navigationIndex = _entries.Count;
        }
        catch
        {
            // If we can't load history, just continue with empty history
        }
    }

    /// <summary>
    /// Saves history entries to the persistent history file.
    /// Only the most recent <see cref="MaxEntries"/> entries are saved.
    /// </summary>
    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var entriesToSave = _entries.TakeLast(MaxEntries).ToList();
            File.WriteAllLines(FilePath, entriesToSave);
        }
        catch
        {
            // If we can't save history, silently continue
        }
    }
}