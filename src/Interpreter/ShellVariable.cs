namespace Radiance.Interpreter;

/// <summary>
/// Represents a shell variable with associated attributes.
/// Mirrors BASH's variable attribute system: read-only, export, integer, array.
/// </summary>
public sealed class ShellVariable
{
    /// <summary>
    /// The scalar value of the variable. Used when <see cref="ArrayElements"/> is null.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Array elements. Null for scalar variables, non-null for array variables.
    /// </summary>
    public List<string>? ArrayElements { get; set; }

    /// <summary>
    /// Whether this variable is read-only and cannot be modified or unset.
    /// </summary>
    public bool IsReadOnly { get; set; }

    /// <summary>
    /// Whether this variable is exported to child processes.
    /// </summary>
    public bool IsExported { get; set; }

    /// <summary>
    /// Whether this variable is treated as an integer (arithmetic evaluation on assignment).
    /// </summary>
    public bool IsInteger { get; set; }

    /// <summary>
    /// Whether this variable is an array.
    /// </summary>
    public bool IsArray => ArrayElements is not null;

    /// <summary>
    /// The number of array elements, or 0 if not an array.
    /// </summary>
    public int ArrayLength => ArrayElements?.Count ?? 0;

    public ShellVariable() { }

    public ShellVariable(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets an array element by index. Returns empty string if not an array or out of range.
    /// </summary>
    public string GetArrayElement(int index)
    {
        if (ArrayElements is null || index < 0 || index >= ArrayElements.Count)
            return string.Empty;
        return ArrayElements[index];
    }

    /// <summary>
    /// Sets an array element at the given index, expanding the list if needed.
    /// </summary>
    public void SetArrayElement(int index, string value)
    {
        ArrayElements ??= new List<string>();
        while (ArrayElements.Count <= index)
            ArrayElements.Add(string.Empty);
        ArrayElements[index] = value;
    }

    /// <summary>
    /// Creates a deep copy of this variable.
    /// </summary>
    public ShellVariable Clone() => new()
    {
        Value = Value,
        ArrayElements = ArrayElements is not null ? new List<string>(ArrayElements) : null,
        IsReadOnly = IsReadOnly,
        IsExported = IsExported,
        IsInteger = IsInteger
    };
}
