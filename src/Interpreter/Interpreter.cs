using System.Text.RegularExpressions;
using Radiance.Builtins;
using Radiance.Expansion;
using Radiance.Lexer;
using Radiance.Parser.Ast;
using Radiance.Utils;

namespace Radiance.Interpreter;

/// <summary>
/// AST walker that interprets and executes the parsed AST.
/// Implements the visitor pattern to dispatch over node types.
/// Supports control flow: if/elif/else, for, while/until, case.
/// </summary>
public sealed class ShellInterpreter : IAstVisitor<int>
{
    private readonly ShellContext _context;
    private readonly BuiltinRegistry _builtins;
    private readonly ProcessManager _processManager;
    private readonly PipelineExecutor _pipelineExecutor;
    private readonly Expander _expander;

    /// <summary>
    /// Creates a new interpreter with the given context, builtin registry, and process manager.
    /// </summary>
    /// <param name="context">The shell execution context.</param>
    /// <param name="builtins">The builtin command registry.</param>
    /// <param name="processManager">The external process manager.</param>
    public ShellInterpreter(ShellContext context, BuiltinRegistry builtins, ProcessManager processManager)
    {
        _context = context;
        _builtins = builtins;
        _processManager = processManager;
        _expander = new Expander(context, builtins, processManager);
        _pipelineExecutor = new PipelineExecutor(context, builtins, processManager, this, _expander);
    }

    /// <summary>
    /// Creates a new interpreter sharing an existing expander (used by command substitution
    /// to avoid re-creating the expander).
    /// </summary>
    internal ShellInterpreter(ShellContext context, BuiltinRegistry builtins, ProcessManager processManager, Expander expander)
    {
        _context = context;
        _builtins = builtins;
        _processManager = processManager;
        _expander = expander;
        _pipelineExecutor = new PipelineExecutor(context, builtins, processManager, this, _expander);
    }

    /// <summary>
    /// Gets the expander used by this interpreter.
    /// </summary>
    internal Expander Expander => _expander;

    /// <summary>
    /// Gets the shell context.
    /// </summary>
    internal ShellContext Context => _context;

    /// <summary>
    /// Executes a top-level AST node and returns the exit code.
    /// </summary>
    /// <param name="node">The AST node to execute.</param>
    /// <returns>The exit code of the last executed command.</returns>
    public int Execute(AstNode node)
    {
        return node.Accept(this);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Executes pipelines in order, respecting separators:
    /// - <c>;</c> and <c>newline</c>: run next unconditionally
    /// - <c>&&</c>: run next only if previous succeeded (exit code 0)
    /// - <c>||</c>: run next only if previous failed (non-zero exit code)
    /// - <c>&</c>: background execution (Phase 6)
    /// Respects <c>set -e</c> (errexit): exits on non-zero unless part of &&/||/if/while condition.
    /// </remarks>
    public int VisitList(ListNode node)
    {
        var exitCode = 0;

        for (var i = 0; i < node.Pipelines.Count; i++)
        {
            // Check conditional execution based on separator BEFORE this pipeline
            if (i > 0)
            {
                var prevSeparator = i - 1 < node.Separators.Count
                    ? node.Separators[i - 1]
                    : Lexer.TokenType.Semicolon;

                switch (prevSeparator)
                {
                    case Lexer.TokenType.And when exitCode != 0:
                        // && but previous failed — skip this pipeline
                        continue;
                    case Lexer.TokenType.Or when exitCode == 0:
                        // || but previous succeeded — skip this pipeline
                        continue;
                }
            }

            // Check if this pipeline should run in background (& separator AFTER it)
            var isBackground = i < node.Separators.Count
                               && node.Separators[i] == Lexer.TokenType.Ampersand;

            if (isBackground)
            {
                var bgPipeline = node.Pipelines[i];
                var bgCommandText = DescribePipeline(bgPipeline);
                var job = _context.JobManager.AddJob(bgCommandText);

                // Run the pipeline in a background thread
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    var bgExitCode = bgPipeline.Accept(this);
                    _context.JobManager.CompleteJob(job.JobNumber, bgExitCode);
                });

                _context.LastBackgroundPid = job.JobNumber;
                exitCode = 0; // Background launch returns 0
            }
            else
            {
                exitCode = node.Pipelines[i].Accept(this);
                _context.LastExitCode = exitCode;

                // set -e: exit on error for unconditional commands
                // Don't trigger for &&, ||, if conditions, while conditions — those check exit codes
                if (exitCode != 0 && _context.Options.ExitOnError)
                {
                    var prevSep = i > 0 && (i - 1) < node.Separators.Count
                        ? node.Separators[i - 1]
                        : Lexer.TokenType.Semicolon;
                    var nextSep = i < node.Separators.Count
                        ? node.Separators[i]
                        : Lexer.TokenType.Semicolon;

                    // Only errexit for unconditional separators (;, newline), not && / ||
                    if (prevSep is not Lexer.TokenType.And and not Lexer.TokenType.Or
                        && nextSep is not Lexer.TokenType.And and not Lexer.TokenType.Or)
                    {
                        break;
                    }
                }
            }

            // Check if break, continue, or return was requested — stop executing the list
            if (_context.BreakRequested || _context.ReturnRequested || _context.ContinueRequested)
                break;
        }

        return exitCode;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Executes a pipeline using the <see cref="PipelineExecutor"/>.
    /// Supports multi-command pipelines connected by pipes, with file
    /// redirections on individual commands.
    /// </remarks>
    public int VisitPipeline(PipelineNode node)
    {
        if (node.Commands.Count == 0)
            return 0;

        // Delegate to pipeline executor for full pipe and redirect support
        return _pipelineExecutor.Execute(node);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Executes a simple command:
    /// <list type="number">
    /// <item>Process prefix assignments</item>
    /// <item>Expand all words using the full expansion pipeline</item>
    /// <item>If there are no words, treat assignments as permanent shell variable assignments</item>
    /// <item>If there are words, dispatch in BASH order: alias → function → builtin → external</item>
    /// </list>
    /// </remarks>
    public int VisitSimpleCommand(SimpleCommandNode node)
    {
        // If there are redirections, delegate to pipeline executor for proper stream handling
        if (node.Redirects.Count > 0)
        {
            return _pipelineExecutor.Execute(new PipelineNode { Commands = [node] });
        }

        // Process prefix assignments
        var assignments = node.Assignments;

        if (node.Words.Count == 0)
        {
            // No command — permanent variable assignments
            foreach (var assignment in assignments)
            {
                var value = _expander.ExpandString(assignment.Value);
                _context.SetVariable(assignment.Name, value);
            }

            // Process array assignments
            foreach (var arrayAssign in node.ArrayAssignments)
            {
                var expandedElements = arrayAssign.Elements
                    .Select(e => _expander.ExpandString(e))
                    .ToList();
                _context.SetArrayVariable(arrayAssign.Name, expandedElements);
            }

            return 0;
        }

        // Expand all words using the full expansion pipeline
        // (tilde, variable, command substitution, arithmetic, glob)
        var expandedWords = _expander.ExpandWords(node.Words);

        if (expandedWords.Count == 0)
        {
            // All words expanded to nothing — just process assignments
            foreach (var assignment in assignments)
            {
                var value = _expander.ExpandString(assignment.Value);
                _context.SetVariable(assignment.Name, value);
            }

            return 0;
        }

        // If there are prefix assignments with a command, set them temporarily.
        // Save current values, set the assignments, execute the command, then restore.
        var savedAssignments = new Dictionary<string, string?>();
        foreach (var assignment in assignments)
        {
            var value = _expander.ExpandString(assignment.Value);
            savedAssignments[assignment.Name] = _context.GetVariable(assignment.Name);
            _context.SetVariable(assignment.Name, value);
            if (_context.IsExported(assignment.Name))
                Environment.SetEnvironmentVariable(assignment.Name, value);
        }

        var commandName = expandedWords[0];

        // set -x: trace command
        if (_context.Options.TraceCommands)
        {
            var traceLine = string.Join(" ", expandedWords);
            var ps4 = _context.GetVariable("PS4");
            if (string.IsNullOrEmpty(ps4)) ps4 = "+ ";
            // Expand PS4 with simple variable substitution (not full prompt expansion)
            var prefix = Radiance.Shell.PromptExpander.Expand(ps4, _context);
            Console.Error.WriteLine($"{prefix}{traceLine}");
        }

        int exitCode;
        try
        {
            // Try alias expansion (BASH order: alias → function → builtin → external)
            var alias = _context.GetAlias(commandName);
            if (alias is not null)
            {
                // Re-parse the alias expansion with the remaining arguments
                var remaining = expandedWords.Count > 1
                    ? " " + string.Join(" ", expandedWords.Skip(1))
                    : "";
                var fullCommand = alias + remaining;

                var aliasLexer = new Lexer.Lexer(fullCommand);
                var aliasTokens = aliasLexer.Tokenize();
                var aliasParser = new Parser.Parser(aliasTokens);
                var aliasAst = aliasParser.Parse();

                if (aliasAst is not null)
                    return Execute(aliasAst);
            }

            var args = expandedWords.ToArray();

            // Try function (BASH order: function → builtin → external)
            if (_context.HasFunction(commandName))
            {
                return ExecuteFunction(commandName, expandedWords);
            }

            // Try builtin
            if (_builtins.TryExecute(commandName, args, _context, out exitCode))
            {
                return exitCode;
            }

            // Try script file execution (for commands like ./script.sh with shebang)
            if (commandName.Contains('/') && _context.ScriptFileExecutor is not null)
            {
                var resolvedPath = commandName;
                if (!Path.IsPathRooted(resolvedPath))
                    resolvedPath = Path.GetFullPath(Path.Combine(_context.CurrentDirectory, resolvedPath));

                if (File.Exists(resolvedPath) && TryReadShebang(resolvedPath, out var shebang))
                {
                    // If shebang references radiance, bash, or sh → execute internally
                    if (shebang.Contains("radiance") || shebang.Contains("bash") || shebang.Contains("/sh"))
                    {
                        return _context.ScriptFileExecutor(resolvedPath, args);
                    }
                }
            }

            // Try external command
            exitCode = _processManager.Execute(commandName, args, _context);
            return exitCode;
        }
        finally
        {
            // Restore prefix assignments
            foreach (var kvp in savedAssignments)
            {
                if (string.IsNullOrEmpty(kvp.Value))
                {
                    _context.UnsetVariable(kvp.Key);
                }
                else
                {
                    _context.SetVariable(kvp.Key, kvp.Value);
                    if (_context.IsExported(kvp.Key))
                        Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
                }
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Executes a variable assignment (standalone, not as a command prefix).
    /// </remarks>
    public int VisitAssignment(AssignmentNode node)
    {
        var value = _expander.ExpandString(node.Value);
        _context.SetVariable(node.Name, value);
        return 0;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Executes an if/elif/else construct:
    /// <list type="number">
    /// <item>Evaluate the condition (a command list; exit code 0 = true)</item>
    /// <item>If condition succeeds, execute the then-body</item>
    /// <item>Otherwise, try elif branches in order</item>
    /// <item>If no branch matches, execute the else-body (if present)</item>
    /// </list>
    /// </remarks>
    public int VisitIf(IfNode node)
    {
        // Evaluate the primary condition
        var exitCode = node.Condition.Accept(this);

        if (exitCode == 0)
        {
            // Condition true — execute then-body
            return node.ThenBody.Accept(this);
        }

        // Try elif branches
        foreach (var (condition, body) in node.ElifBranches)
        {
            exitCode = condition.Accept(this);
            if (exitCode == 0)
            {
                return body.Accept(this);
            }
        }

        // Execute else-body if present
        if (node.ElseBody is not null)
        {
            return node.ElseBody.Accept(this);
        }

        // No branch matched — return 0 per POSIX
        return 0;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Executes a for loop:
    /// <list type="number">
    /// <item>Expand all iterable words (including glob expansion)</item>
    /// <item>If no words specified, iterate over positional parameters ($@)</item>
    /// <item>For each value, set the loop variable and execute the body</item>
    /// </list>
    /// </remarks>
    public int VisitFor(ForNode node)
    {
        // Expand iterable words
        List<string> items;

        if (node.IterableWords.Count == 0)
        {
            // No 'in' clause — iterate over positional parameters
            items = _context.PositionalParams.ToList();
        }
        else
        {
            items = _expander.ExpandWords(node.IterableWords);
        }

        var exitCode = 0;

        foreach (var item in items)
        {
            _context.SetVariable(node.VariableName, item);

            // Clear continue flag before body execution
            _context.ContinueRequested = false;
            exitCode = node.Body.Accept(this);

            // Check break
            if (_context.BreakRequested)
            {
                _context.BreakRequested = false;
                if (_context.BreakDepth > 1)
                {
                    _context.BreakDepth--;
                    return exitCode; // Propagate break to outer loop
                }
                _context.BreakDepth = 1;
                break;
            }

            // Check return — propagate to function level
            if (_context.ReturnRequested)
                break;

            // Check continue — just move to next iteration
            if (_context.ContinueRequested)
            {
                _context.ContinueRequested = false;
                if (_context.ContinueDepth > 1)
                {
                    _context.ContinueDepth--;
                    return exitCode; // Propagate continue to outer loop
                }
                _context.ContinueDepth = 1;
                continue;
            }
        }

        return exitCode;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Executes a while or until loop:
    /// <list type="number">
    /// <item>Evaluate the condition (a command list)</item>
    /// <item>For <c>while</c>: loop while exit code is 0</item>
    /// <item>For <c>until</c>: loop while exit code is non-zero</item>
    /// <item>Execute the body each iteration</item>
    /// </list>
    /// Includes a safety limit to prevent infinite loops during development.
    /// </remarks>
    public int VisitWhile(WhileNode node)
    {
        var exitCode = 0;
        const int maxIterations = 1_000_000;
        var iterations = 0;

        while (true)
        {
            var conditionExitCode = node.Condition.Accept(this);

            var shouldContinue = node.IsUntil
                ? conditionExitCode != 0  // until: loop while condition fails
                : conditionExitCode == 0; // while: loop while condition succeeds

            if (!shouldContinue)
                break;

            // Clear continue flag before body execution
            _context.ContinueRequested = false;
            exitCode = node.Body.Accept(this);
            iterations++;

            if (iterations >= maxIterations)
            {
                Radiance.Utils.ColorOutput.WriteWarning($"loop exceeded {maxIterations} iterations, breaking");
                break;
            }

            // Check break
            if (_context.BreakRequested)
            {
                _context.BreakRequested = false;
                if (_context.BreakDepth > 1)
                {
                    _context.BreakDepth--;
                    return exitCode; // Propagate break to outer loop
                }
                _context.BreakDepth = 1;
                break;
            }

            // Check return — propagate to function level
            if (_context.ReturnRequested)
                break;

            // Check continue — skip to next iteration
            if (_context.ContinueRequested)
            {
                _context.ContinueRequested = false;
                if (_context.ContinueDepth > 1)
                {
                    _context.ContinueDepth--;
                    return exitCode; // Propagate continue to outer loop
                }
                _context.ContinueDepth = 1;
                continue;
            }
        }

        return exitCode;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Executes a case statement:
    /// <list type="number">
    /// <item>Expand the word to match</item>
    /// <item>For each case item, expand its patterns and try to match</item>
    /// <item>Use glob-style pattern matching (via <see cref="GlobExpander"/> logic)</item>
    /// <item>Execute the body of the first matching case item</item>
    /// <item>If <c>;;</c> is used, stop after the first match (standard BASH behavior)</item>
    /// </list>
    /// </remarks>
    public int VisitCase(CaseNode node)
    {
        // Expand the word to match (with glob expansion for the subject)
        var expandedWord = _expander.ExpandWord(node.Word);
        var wordToMatch = expandedWord.Count > 0 ? expandedWord[0] : string.Empty;

        foreach (var item in node.Items)
        {
            // Check each pattern in the case item
            // Skip glob expansion for patterns — they are used for matching, not filenames
            foreach (var patternParts in item.Patterns)
            {
                var expandedPattern = _expander.ExpandWord(patternParts, skipGlob: true);
                if (expandedPattern.Count == 0)
                    continue;

                var pattern = expandedPattern[0];

                if (MatchCasePattern(wordToMatch, pattern))
                {
                    // Pattern matched — execute the body
                    return item.Body.Accept(this);
                }
            }
        }

        // No pattern matched — return 0
        return 0;
    }

    /// <summary>
    /// Cache of case-pattern regex objects to avoid recompilation.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Regex> CaseRegexCache = new();
    private const int MaxCaseRegexCacheSize = 64;

    /// <summary>
    /// Matches a string against a case pattern using glob-style matching.
    /// Supports *, ?, and [...] character classes.
    /// </summary>
    /// <param name="value">The string to test.</param>
    /// <param name="pattern">The glob pattern.</param>
    /// <returns>True if the value matches the pattern.</returns>
    private static bool MatchCasePattern(string value, string pattern)
    {
        // Exact match (fast path)
        if (pattern == value)
            return true;

        // * matches everything
        if (pattern == "*")
            return true;

        // Convert glob pattern to cached regex
        var regex = CaseRegexCache.GetOrAdd(pattern, p =>
        {
            var patternStr = CaseGlobToRegex(p);
            return new Regex($"^{patternStr}$", RegexOptions.IgnoreCase);
        });
        if (CaseRegexCache.Count > MaxCaseRegexCacheSize)
        {
            foreach (var key in CaseRegexCache.Keys.Take(CaseRegexCache.Count / 2).ToList())
                CaseRegexCache.TryRemove(key, out _);
        }
        return regex.IsMatch(value);
    }

    /// <summary>
    /// Converts a glob pattern to a regex pattern string.
    /// Reuses the same logic as <see cref="GlobExpander"/> but for case matching.
    /// </summary>
    private static string CaseGlobToRegex(string pattern)
    {
        var result = new System.Text.StringBuilder();
        foreach (var c in pattern)
        {
            switch (c)
            {
                case '*':
                    result.Append(".*");
                    break;
                case '?':
                    result.Append('.');
                    break;
                case '.':
                case '^':
                case '$':
                case '+':
                case '(':
                case ')':
                case '{':
                case '}':
                case '\\':
                case '|':
                    result.Append('\\');
                    result.Append(c);
                    break;
                case '[':
                    result.Append('[');
                    break;
                case ']':
                    result.Append(']');
                    break;
                default:
                    result.Append(c);
                    break;
            }
        }

        return result.ToString();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Executes a select menu loop:
    /// <list type="number">
    /// <item>Expand iterable words (or use positional params if no 'in' clause)</item>
    /// <item>Display a numbered menu</item>
    /// <item>Read user selection</item>
    /// <item>Set the loop variable to the selected item (or empty if invalid)</item>
    /// <item>Execute the body</item>
    /// <item>Repeat until EOF or empty input</item>
    /// </list>
    /// </remarks>
    public int VisitSelect(SelectNode node)
    {
        // Expand iterable words
        List<string> items;

        if (node.IterableWords.Count == 0)
        {
            items = _context.PositionalParams.ToList();
        }
        else
        {
            items = _expander.ExpandWords(node.IterableWords);
        }

        var exitCode = 0;
        var ps2 = _context.GetVariable("PS2");
        if (string.IsNullOrEmpty(ps2)) ps2 = "?# ";

        while (true)
        {
            // Display numbered menu
            for (var idx = 0; idx < items.Count; idx++)
                Console.WriteLine($"{idx + 1}) {items[idx]}");

            // Read selection
            Console.Write(ps2);
            var input = Console.ReadLine();

            if (input is null)
            {
                Console.WriteLine();
                break;
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                // Empty input — clear variable and re-display
                _context.SetVariable(node.VariableName, string.Empty);
                continue;
            }

            if (int.TryParse(input.Trim(), out var selection) && selection >= 1 && selection <= items.Count)
            {
                _context.SetVariable(node.VariableName, items[selection - 1]);
            }
            else
            {
                // Invalid selection — clear variable
                _context.SetVariable(node.VariableName, string.Empty);
                continue;
            }

            // Clear continue flag before body execution
            _context.ContinueRequested = false;
            exitCode = node.Body.Accept(this);

            // Check break
            if (_context.BreakRequested)
            {
                _context.BreakRequested = false;
                _context.BreakDepth = 1;
                break;
            }

            // Check return
            if (_context.ReturnRequested)
                break;

            // Check continue
            if (_context.ContinueRequested)
            {
                _context.ContinueRequested = false;
                _context.ContinueDepth = 1;
                continue;
            }
        }

        return exitCode;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Registers a function definition in the shell context.
    /// Does not execute the body — the body is stored and executed when the function is called.
    /// </remarks>
    public int VisitFunction(FunctionNode node)
    {
        _context.SetFunction(node.Name, node.Body);
        return 0;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Executes a subshell: runs the body in an isolated context.
    /// Variable changes, function definitions, alias changes, and directory changes
    /// inside the subshell do NOT affect the parent shell.
    /// Redirects on the subshell are applied to the body execution.
    /// </remarks>
    public int VisitSubshell(SubshellNode node)
    {
        // If there are redirects, delegate to pipeline executor
        if (node.Redirects.Count > 0)
        {
            // Wrap in a simple command-like pipeline for redirect handling
            // For now, execute with redirects as a simple command wrapper
            return ExecuteSubshellWithRedirects(node);
        }

        return ExecuteSubshellBody(node.Body);
    }

    /// <summary>
    /// Executes a subshell body in an isolated context.
    /// Saves all mutable state, executes, then restores.
    /// </summary>
    private int ExecuteSubshellBody(ListNode body)
    {
        // Save mutable state
        var savedDirectory = _context.CurrentDirectory;
        var savedExitCode = _context.LastExitCode;
        var savedBgPid = _context.LastBackgroundPid;

        // Save variable scopes (deep copy)
        var savedScopes = _context.DumpScopes();
        var savedAliases = new Dictionary<string, string>(_context.Aliases);
        var savedFunctions = new Dictionary<string, FunctionDef>();
        foreach (var fn in _context.FunctionNames)
        {
            var func = _context.GetFunction(fn);
            if (func is not null)
                savedFunctions[fn] = func;
        }

        // Clear control flow flags
        var savedBreak = _context.BreakRequested;
        var savedContinue = _context.ContinueRequested;
        var savedReturn = _context.ReturnRequested;

        try
        {
            var exitCode = body.Accept(this);
            _context.LastExitCode = exitCode;
            return exitCode;
        }
        finally
        {
            // Restore all mutable state
            _context.CurrentDirectory = savedDirectory;
            _context.LastBackgroundPid = savedBgPid;
            _context.RestoreScopes(savedScopes);

            // Restore aliases
            _context.UnsetAllAliases();
            foreach (var kvp in savedAliases)
                _context.SetAlias(kvp.Key, kvp.Value);

            // Restore functions
            foreach (var fn in _context.FunctionNames.ToList())
                _context.UnsetFunction(fn);
            foreach (var kvp in savedFunctions)
                _context.SetFunction(kvp.Key, kvp.Value.Body);

            // Restore control flow
            _context.BreakRequested = savedBreak;
            _context.ContinueRequested = savedContinue;
            _context.ReturnRequested = savedReturn;
        }
    }

    /// <summary>
    /// Executes a subshell with I/O redirections applied.
    /// </summary>
    private int ExecuteSubshellWithRedirects(SubshellNode node)
    {
        // For subshells with redirects, capture output and apply redirects
        var hasStdoutRedirect = node.Redirects.Any(r =>
            r.RedirectType is TokenType.GreaterThan or TokenType.DoubleGreaterThan);
        var hasStdinRedirect = node.Redirects.Any(r => r.RedirectType == TokenType.LessThan);

        if (!hasStdoutRedirect && !hasStdinRedirect)
            return ExecuteSubshellBody(node.Body);

        // Capture output via Console.SetOut
        var captured = new StringWriter();
        var originalOut = Console.Out;
        OutputCapture.Push();
        try
        {
            if (hasStdoutRedirect)
                Console.SetOut(captured);

            var exitCode = ExecuteSubshellBody(node.Body);

            if (hasStdoutRedirect)
            {
                var output = captured.ToString();
                // Write to redirect target
                foreach (var redirect in node.Redirects)
                {
                    if (redirect.RedirectType is TokenType.GreaterThan or TokenType.DoubleGreaterThan)
                    {
                        var expanded = _expander.ExpandWord(redirect.Target!);
                        if (expanded.Count > 0)
                        {
                            var append = redirect.RedirectType == TokenType.DoubleGreaterThan;
                            File.WriteAllText(expanded[0], output);
                        }
                    }
                }
            }

            return exitCode;
        }
        finally
        {
            Console.SetOut(originalOut);
            OutputCapture.Pop();
        }
    }

    /// <summary>
    /// Executes a function call by name with the given arguments.
    /// Pushes a new variable scope and positional parameters, executes the body,
    /// and pops the scope when done. Supports <c>return</c> via <see cref="ShellContext.ReturnRequested"/>.
    /// </summary>
    /// <param name="name">The function name.</param>
    /// <param name="args">The arguments (including function name as args[0]).</param>
    /// <returns>The exit code of the function body or the <c>return</c> exit code.</returns>
    internal int ExecuteFunction(string name, List<string> args)
    {
        var func = _context.GetFunction(name);
        if (func is null)
        {
            Console.Error.WriteLine($"radiance: {name}: function not found");
            return 127;
        }

        // Save FUNCNAME tracking
        var prevFuncName = _context.GetVariable("FUNCNAME");

        // Push a new variable scope for the function
        _context.PushScope();

        // Set positional parameters — only the arguments, NOT the function name.
        // In BASH: $1 = first arg, $2 = second arg, etc.
        var funcArgs = new List<string>();
        for (var i = 1; i < args.Count; i++)
            funcArgs.Add(args[i]);

        _context.PushPositionalParams(funcArgs);
        _context.SetLocalVariable("FUNCNAME", name);

        // Clear the return flag
        _context.ReturnRequested = false;
        _context.ReturnExitCode = 0;

        try
        {
            var exitCode = func.Body.Accept(this);

            // If return was requested, use its exit code
            if (_context.ReturnRequested)
                exitCode = _context.ReturnExitCode;

            return exitCode;
        }
        finally
        {
            // Pop scope and restore positional parameters
            _context.PopScope();
            _context.PopPositionalParams();
            _context.ReturnRequested = false;

            // Restore FUNCNAME
            if (!string.IsNullOrEmpty(prevFuncName))
                _context.SetVariable("FUNCNAME", prevFuncName);
        }
    }

    /// <summary>
    /// Expands variables in a simple string (legacy method for backward compatibility).
    /// For full expansion, use the <see cref="Expander"/> directly.
    /// </summary>
    internal string ExpandVariables(string input)
    {
        return _expander.ExpandString(input);
    }

    /// <summary>
    /// Reads the shebang line (#!) from a file if present.
    /// Returns true if the file starts with #!, false otherwise.
    /// </summary>
    /// <param name="path">The file path to read.</param>
    /// <param name="shebang">The shebang line content (without the #! prefix and leading whitespace).</param>
    /// <returns>True if a shebang was found.</returns>
    private static bool TryReadShebang(string path, out string shebang)
    {
        shebang = string.Empty;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 256);
            using var reader = new StreamReader(stream);
            var firstLine = reader.ReadLine();
            if (firstLine is not null && firstLine.StartsWith("#!"))
            {
                shebang = firstLine[2..].TrimStart();
                return true;
            }
        }
        catch
        {
            // Ignore file access errors — fall through to external execution
        }
        return false;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Executes a brace group: runs the body in the CURRENT shell scope.
    /// Variable changes inside a brace group DO affect the parent scope.
    /// This is the key semantic difference from a subshell.
    /// </remarks>
    public int VisitBraceGroup(BraceGroupNode node)
    {
        // If there are redirections, delegate to pipeline executor
        if (node.Redirects.Count > 0)
        {
            return _pipelineExecutor.Execute(new PipelineNode { Commands = [node] });
        }

        return node.Body.Accept(this);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Executes an array variable assignment: <c>ARR=(val1 val2 val3)</c>.
    /// Expands each element and stores as an array variable.
    /// </remarks>
    public int VisitArrayAssignment(ArrayAssignmentNode node)
    {
        var expandedElements = node.Elements
            .Select(e => _expander.ExpandString(e))
            .ToList();
        _context.SetArrayVariable(node.Name, expandedElements);
        return 0;
    }

    /// <summary>
    /// Produces a human-readable description of a pipeline node for job display.
    /// </summary>
    private static string DescribePipeline(AstNode node)
    {
        return node switch
        {
            PipelineNode p => string.Join(" | ", p.Commands.Select(DescribePipeline)),
            SimpleCommandNode c => string.Join(" ", c.Words.Select(w =>
                string.Concat(w.Select(p => p.Text)))),
            _ => node.GetType().Name
        };
    }
}
