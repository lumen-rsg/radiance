using Radiance.Parser.Ast;

namespace Radiance.Interpreter;

/// <summary>
/// Holds the current shell execution state: environment variables, shell variables,
/// working directory, last exit code, functions, aliases, etc.
/// Supports variable scoping for shell functions via a scope stack.
/// </summary>
public sealed class ShellContext
{
    /// <summary>
    /// Shell variable scopes. The last element is the current (innermost) scope.
    /// Index 0 is the global scope. Function calls push new scopes.
    /// </summary>
    private readonly List<Dictionary<string, string>> _variableScopes = [new()];

    /// <summary>
    /// Environment variables marked for export to child processes.
    /// </summary>
    private readonly HashSet<string> _exportedVars = new();

    /// <summary>
    /// Registered shell functions. Key is the function name.
    /// </summary>
    private readonly Dictionary<string, FunctionDef> _functions = new(StringComparer.Ordinal);

    /// <summary>
    /// Registered shell aliases. Key is the alias name, value is the expansion.
    /// </summary>
    private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);

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
    /// Positional parameter scope stack for function calls.
    /// </summary>
    private readonly Stack<List<string>> _positionalParamScopes = new();

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
    /// The job manager for tracking background jobs.
    /// </summary>
    public JobManager JobManager { get; } = new();

    /// <summary>
    /// Flag indicating that a <c>return</c> was triggered inside a function.
    /// The interpreter checks this after each command execution inside functions.
    /// </summary>
    public bool ReturnRequested { get; set; } = false;

    /// <summary>
    /// The exit code to return from the current function.
    /// </summary>
    public int ReturnExitCode { get; set; } = 0;

    /// <summary>
    /// Flag indicating that a <c>break</c> was triggered inside a loop.
    /// Checked by <see cref="ShellInterpreter.VisitFor"/> and <see cref="ShellInterpreter.VisitWhile"/>.
    /// </summary>
    public bool BreakRequested { get; set; } = false;

    /// <summary>
    /// The number of loop levels to break out of.
    /// </summary>
    public int BreakDepth { get; set; } = 1;

    /// <summary>
    /// Flag indicating that a <c>continue</c> was triggered inside a loop.
    /// Checked by <see cref="ShellInterpreter.VisitFor"/> and <see cref="ShellInterpreter.VisitWhile"/>.
    /// </summary>
    public bool ContinueRequested { get; set; } = false;

    /// <summary>
    /// The number of loop levels to continue past.
    /// </summary>
    public int ContinueDepth { get; set; } = 1;

    /// <summary>
    /// Callback for executing a script file in the current context.
    /// Used by the <c>source</c>/<c>.</c> builtin to delegate execution to the shell.
    /// </summary>
    public Func<string, string[], int>? ScriptExecutor { get; set; }

    /// <summary>
    /// Callback for executing a script file (with shebang handling) as an external command.
    /// Used when the interpreter encounters a script file that should be executed internally
    /// (e.g., when the shebang references <c>radiance</c> or <c>bash</c>).
    /// Parameters: script path, arguments array. Returns exit code.
    /// </summary>
    public Func<string, string[], int>? ScriptFileExecutor { get; set; }

    // ──── Variable Access (scope-aware) ────

    /// <summary>
    /// The current (innermost) variable scope.
    /// </summary>
    private Dictionary<string, string> CurrentScope => _variableScopes[^1];

    /// <summary>
    /// Sets a shell variable in the current scope. If it is already exported, updates the environment.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The variable value.</param>
    public void SetVariable(string name, string value)
    {
        // Check if the variable exists in any scope — update it there
        for (var i = _variableScopes.Count - 1; i >= 0; i--)
        {
            if (_variableScopes[i].ContainsKey(name))
            {
                _variableScopes[i][name] = value;
                if (_exportedVars.Contains(name))
                    Environment.SetEnvironmentVariable(name, value);
                return;
            }
        }

        // Not found — set in current scope
        CurrentScope[name] = value;
        if (_exportedVars.Contains(name))
            Environment.SetEnvironmentVariable(name, value);
    }

    /// <summary>
    /// Sets a local variable in the current (innermost) scope.
    /// Used by the <c>local</c> builtin inside functions.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The variable value.</param>
    public void SetLocalVariable(string name, string value)
    {
        CurrentScope[name] = value;
    }

    /// <summary>
    /// Gets the value of a shell or environment variable.
    /// Searches scopes from innermost to outermost, then falls back to environment.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The variable value, or empty string if not found.</returns>
    public string GetVariable(string name)
    {
        // Search from innermost scope outward
        for (var i = _variableScopes.Count - 1; i >= 0; i--)
        {
            if (_variableScopes[i].TryGetValue(name, out var value))
                return value;
        }

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
        var value = GetVariable(name);
        if (!string.IsNullOrEmpty(value))
            Environment.SetEnvironmentVariable(name, value);
    }

    /// <summary>
    /// Exports a variable with a specific value (equivalent to: export NAME=VALUE).
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The variable value.</param>
    public void ExportVariable(string name, string value)
    {
        SetVariable(name, value);
        _exportedVars.Add(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    /// <summary>
    /// Unsets a shell variable from all scopes. If exported, also removes from the environment.
    /// </summary>
    /// <param name="name">The variable name.</param>
    public void UnsetVariable(string name)
    {
        foreach (var scope in _variableScopes)
            scope.Remove(name);
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
    /// Gets all shell variable names from all scopes (for display purposes).
    /// </summary>
    public IEnumerable<string> ShellVariableNames
    {
        get
        {
            var names = new HashSet<string>();
            foreach (var scope in _variableScopes)
                foreach (var name in scope.Keys)
                    names.Add(name);
            return names;
        }
    }

    /// <summary>
    /// Gets all exported variable names.
    /// </summary>
    public IEnumerable<string> ExportedVariableNames => _exportedVars;

    // ──── Variable Scoping ────

    /// <summary>
    /// Pushes a new variable scope (for function calls).
    /// </summary>
    public void PushScope()
    {
        _variableScopes.Add(new Dictionary<string, string>());
    }

    /// <summary>
    /// Pops the innermost variable scope (when a function returns).
    /// </summary>
    public void PopScope()
    {
        if (_variableScopes.Count > 1)
            _variableScopes.RemoveAt(_variableScopes.Count - 1);
    }

    /// <summary>
    /// Gets the current scope depth (1 = global scope).
    /// </summary>
    public int ScopeDepth => _variableScopes.Count;

    // ──── Functions ────

    /// <summary>
    /// Registers a shell function.
    /// </summary>
    /// <param name="name">The function name.</param>
    /// <param name="body">The function body AST node.</param>
    public void SetFunction(string name, ListNode body)
    {
        _functions[name] = new FunctionDef(name, body);
    }

    /// <summary>
    /// Gets a registered function by name.
    /// </summary>
    /// <param name="name">The function name.</param>
    /// <returns>The function definition, or null if not found.</returns>
    public FunctionDef? GetFunction(string name)
    {
        return _functions.TryGetValue(name, out var func) ? func : null;
    }

    /// <summary>
    /// Checks if a function with the given name is registered.
    /// </summary>
    /// <param name="name">The function name.</param>
    /// <returns>True if the function exists.</returns>
    public bool HasFunction(string name) => _functions.ContainsKey(name);

    /// <summary>
    /// Unregisters a function.
    /// </summary>
    /// <param name="name">The function name.</param>
    public void UnsetFunction(string name) => _functions.Remove(name);

    /// <summary>
    /// Gets all registered function names.
    /// </summary>
    public IEnumerable<string> FunctionNames => _functions.Keys;

    // ──── Aliases ────

    /// <summary>
    /// Sets an alias.
    /// </summary>
    /// <param name="name">The alias name.</param>
    /// <param name="value">The alias expansion.</param>
    public void SetAlias(string name, string value) => _aliases[name] = value;

    /// <summary>
    /// Gets an alias expansion by name.
    /// </summary>
    /// <param name="name">The alias name.</param>
    /// <returns>The expansion string, or null if not found.</returns>
    public string? GetAlias(string name) => _aliases.TryGetValue(name, out var value) ? value : null;

    /// <summary>
    /// Removes an alias.
    /// </summary>
    /// <param name="name">The alias name.</param>
    public void UnsetAlias(string name) => _aliases.Remove(name);

    /// <summary>
    /// Removes all aliases.
    /// </summary>
    public void UnsetAllAliases() => _aliases.Clear();

    /// <summary>
    /// Gets all registered aliases.
    /// </summary>
    public IReadOnlyDictionary<string, string> Aliases => _aliases;

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

    /// <summary>
    /// Pushes the current positional parameters onto the stack and sets new ones.
    /// Used when calling functions to preserve and replace $1, $2, etc.
    /// </summary>
    /// <param name="args">The new positional parameters (function arguments).</param>
    public void PushPositionalParams(List<string> args)
    {
        _positionalParamScopes.Push(new List<string>(_positionalParams));
        _positionalParams = new List<string>(args);
    }

    /// <summary>
    /// Pops the positional parameter stack, restoring the previous parameters.
    /// Called when a function returns.
    /// </summary>
    public void PopPositionalParams()
    {
        if (_positionalParamScopes.Count > 0)
            _positionalParams = _positionalParamScopes.Pop();
    }
}

/// <summary>
/// Represents a shell function definition.
/// </summary>
/// <param name="Name">The function name.</param>
/// <param name="Body">The function body AST node.</param>
public sealed record FunctionDef(string Name, ListNode Body);