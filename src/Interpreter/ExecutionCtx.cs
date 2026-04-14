namespace Radiance.Interpreter;

/// <summary>
/// Holds the current shell execution state: environment variables, shell variables,
/// working directory, last exit code, etc.
/// </summary>
public sealed class ShellContext
{
    /// <summary>
    /// Shell variables (not exported to child processes).
    /// </summary>
    private readonly Dictionary<string, string> _shellVariables = new();

    /// <summary>
    /// Environment variables marked for export to child processes.
    /// </summary>
    private readonly HashSet<string> _exportedVars = new();

    /// <summary>
    /// Gets or sets the last exit code from a command.
    /// </summary>
    public int LastExitCode { get; set; } = 0;

    /// <summary>
    /// Gets or sets the current working directory.
    /// </summary>
    public string CurrentDirectory { get; set; } = Directory.GetCurrentDirectory();

    /// <summary>
    /// Positional parameters: $1, $2, ..., $9.
    /// Set via script arguments or the <c>set --</c> command.
    /// </summary>
    private List<string> _positionalParams = [];

    /// <summary>
    /// The shell name ($0) — typically "radiance" or the script path.
    /// </summary>
    public string ShellName { get; set; } = "radiance";

    /// <summary>
    /// PID of the last background process ($!). Returns 0 if no background process.
    /// </summary>
    public int LastBackgroundPid { get; set; } = 0;

    /// <summary>
    /// Shell options ($-) — currently a placeholder for future use.
    /// </summary>
    public string ShellOptions { get; set; } = "";

    /// <summary>
    /// Sets a shell variable. If it is already exported, it also updates the environment.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The variable value.</param>
    public void SetVariable(string name, string value)
    {
        _shellVariables[name] = value;
        if (_exportedVars.Contains(name))
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    /// <summary>
    /// Gets the value of a shell or environment variable.
    /// Looks in shell variables first, then falls back to environment variables.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The variable value, or empty string if not found.</returns>
    public string GetVariable(string name)
    {
        if (_shellVariables.TryGetValue(name, out var value))
            return value;

        return Environment.GetEnvironmentVariable(name) ?? string.Empty;
    }

    /// <summary>
    /// Marks a variable as exported, making it available to child processes.
    /// If the variable already has a value, it is set in the environment.
    /// </summary>
    /// <param name="name">The variable name.</param>
    public void ExportVariable(string name)
    {
        _exportedVars.Add(name);
        if (_shellVariables.TryGetValue(name, out var value))
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    /// <summary>
    /// Exports a variable with a specific value (equivalent to: export NAME=VALUE).
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The variable value.</param>
    public void ExportVariable(string name, string value)
    {
        _shellVariables[name] = value;
        _exportedVars.Add(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    /// <summary>
    /// Unsets a shell variable. If exported, also removes it from the environment.
    /// </summary>
    /// <param name="name">The variable name.</param>
    public void UnsetVariable(string name)
    {
        _shellVariables.Remove(name);
        _exportedVars.Remove(name);
        Environment.SetEnvironmentVariable(name, null);
    }

    /// <summary>
    /// Checks if a variable is exported.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <returns>True if the variable is marked for export.</returns>
    public bool IsExported(string name) => _exportedVars.Contains(name);

    /// <summary>
    /// Gets all shell variable names.
    /// </summary>
    public IEnumerable<string> ShellVariableNames => _shellVariables.Keys;

    /// <summary>
    /// Gets all exported variable names.
    /// </summary>
    public IEnumerable<string> ExportedVariableNames => _exportedVars;

    // ──── Positional Parameters ────

    /// <summary>
    /// Sets the positional parameters ($1, $2, ...).
    /// Typically called from <c>set --</c> or when running a script with arguments.
    /// </summary>
    /// <param name="args">The positional parameter values.</param>
    public void SetPositionalParams(List<string> args)
    {
        _positionalParams = new List<string>(args);
    }

    /// <summary>
    /// Gets a positional parameter by index (1-based).
    /// Returns empty string if the index is out of range.
    /// </summary>
    /// <param name="index">1-based positional parameter index.</param>
    /// <returns>The parameter value, or empty string.</returns>
    public string GetPositionalParam(int index)
    {
        if (index < 1 || index > _positionalParams.Count)
            return string.Empty;
        return _positionalParams[index - 1];
    }

    /// <summary>
    /// Gets the count of positional parameters ($#).
    /// </summary>
    public int PositionalParamCount => _positionalParams.Count;

    /// <summary>
    /// Gets all positional parameters ($@ / $*).
    /// </summary>
    public IReadOnlyList<string> PositionalParams => _positionalParams.AsReadOnly();
}
