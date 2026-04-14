using System.Text.RegularExpressions;
using Radiance.Builtins;
using Radiance.Expansion;
using Radiance.Parser.Ast;

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

        // If there are prefix assignments with a command, set them.
        // TODO Phase 7+: Make these temporary for the command only.
        foreach (var assignment in assignments)
        {
            var value = _expander.ExpandString(assignment.Value);
            _context.SetVariable(assignment.Name, value);
        }

        var commandName = expandedWords[0];

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
        if (_builtins.TryExecute(commandName, args, _context, out var exitCode))
        {
            return exitCode;
        }

        // Try external command
        exitCode = _processManager.Execute(commandName, args, _context);
        return exitCode;
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

        // Convert glob pattern to regex
        var regex = GlobToRegex(pattern);
        return Regex.IsMatch(value, $"^{regex}$", RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Converts a glob pattern to a regex pattern string.
    /// Reuses the same logic as <see cref="GlobExpander"/> but for case matching.
    /// </summary>
    private static string GlobToRegex(string pattern)
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
    /// Registers a function definition in the shell context.
    /// Does not execute the body — the body is stored and executed when the function is called.
    /// </remarks>
    public int VisitFunction(FunctionNode node)
    {
        _context.SetFunction(node.Name, node.Body);
        return 0;
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
