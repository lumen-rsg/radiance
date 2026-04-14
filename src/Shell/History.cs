namespace Radiance.Shell;

/// <summary>
/// Manages in-memory command history with navigation (up/down arrows).
/// </summary>
public sealed class History
{
    private readonly List<string> _entries = new();
    private int _navigationIndex = -1;

    /// <summary>
    /// The maximum number of history entries to keep.
    /// </summary>
    public int MaxEntries { get; set; } = 1000;

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
}
