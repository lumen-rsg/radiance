using Radiance.Builtins;
using Radiance.Expansion;
using Radiance.Parser.Ast;

namespace Radiance.Interpreter;

/// <summary>
/// AST walker that interprets and executes the parsed AST.
/// Implements the visitor pattern to dispatch over node types.
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
            // Check the separator from the previous pipeline to decide whether to execute
            if (i > 0)
            {
                var separator = node.Separators.Count >= i ? node.Separators[i - 1] : Lexer.TokenType.Semicolon;

                switch (separator)
                {
                    case Lexer.TokenType.And when exitCode != 0:
                        // && but previous failed — skip this pipeline
                        continue;
                    case Lexer.TokenType.Or when exitCode == 0:
                        // || but previous succeeded — skip this pipeline
                        continue;
                    case Lexer.TokenType.Ampersand:
                        // & — background execution (Phase 6)
                        // For now, just execute normally
                        break;
                }
            }

            exitCode = node.Pipelines[i].Accept(this);
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
    /// <item>If there are words, dispatch to builtin or external command</item>
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
        // TODO Phase 5+: Make these temporary for the command only.
        foreach (var assignment in assignments)
        {
            var value = _expander.ExpandString(assignment.Value);
            _context.SetVariable(assignment.Name, value);
        }

        var commandName = expandedWords[0];
        var args = expandedWords.ToArray();

        // Try builtin first
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

    /// <summary>
    /// Expands variables in a simple string (legacy method for backward compatibility).
    /// For full expansion, use the <see cref="Expander"/> directly.
    /// </summary>
    internal string ExpandVariables(string input)
    {
        return _expander.ExpandString(input);
    }
}