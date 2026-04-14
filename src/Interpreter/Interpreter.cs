using System.Text;
using Radiance.Builtins;
using Radiance.Lexer;
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
        _pipelineExecutor = new PipelineExecutor(context, builtins, processManager, this);
    }

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
                var separator = node.Separators.Count >= i ? node.Separators[i - 1] : TokenType.Semicolon;

                switch (separator)
                {
                    case TokenType.And when exitCode != 0:
                        // && but previous failed — skip this pipeline
                        continue;
                    case TokenType.Or when exitCode == 0:
                        // || but previous succeeded — skip this pipeline
                        continue;
                    case TokenType.Ampersand:
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
    /// <item>Expand variables in command words</item>
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
        // If there's a command, these are temporary for that command only.
        // If there's no command, these are permanent shell variables.
        var assignments = node.Assignments;

        if (node.Words.Count == 0)
        {
            // No command — permanent variable assignments
            foreach (var assignment in assignments)
            {
                var value = ExpandVariables(assignment.Value);
                _context.SetVariable(assignment.Name, value);
            }

            return 0;
        }

        // Expand variables in all words
        var expandedWords = node.Words.Select(ExpandVariables).ToList();

        // If there are prefix assignments with a command, they should be temporary.
        // For Phase 2 simplicity: set them permanently (temporary env for child commands in Phase 4).
        foreach (var assignment in assignments)
        {
            var value = ExpandVariables(assignment.Value);
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
        var value = ExpandVariables(node.Value);
        _context.SetVariable(node.Name, value);
        return 0;
    }

    // ──── Variable Expansion ────

    /// <summary>
    /// Expands $VAR and ${VAR} references in a string.
    /// Special variables: $? (last exit code), $$ (PID).
    /// </summary>
    /// <param name="input">The string potentially containing variable references.</param>
    /// <returns>The string with all variable references expanded.</returns>
    internal string ExpandVariables(string input)
    {
        if (!input.Contains('$'))
            return input;

        var sb = new StringBuilder();
        var i = 0;

        while (i < input.Length)
        {
            if (input[i] == '$' && i + 1 < input.Length)
            {
                i++; // skip $

                if (input[i] == '?')
                {
                    sb.Append(_context.LastExitCode);
                    i++;
                }
                else if (input[i] == '$')
                {
                    sb.Append(Environment.ProcessId);
                    i++;
                }
                else if (input[i] == '{')
                {
                    // ${VAR} form
                    i++; // skip {
                    var name = new StringBuilder();
                    while (i < input.Length && input[i] != '}')
                    {
                        name.Append(input[i]);
                        i++;
                    }

                    if (i < input.Length) i++; // skip }
                    sb.Append(_context.GetVariable(name.ToString()));
                }
                else if (char.IsLetterOrDigit(input[i]) || input[i] == '_')
                {
                    // $VAR form
                    var name = new StringBuilder();
                    while (i < input.Length && (char.IsLetterOrDigit(input[i]) || input[i] == '_'))
                    {
                        name.Append(input[i]);
                        i++;
                    }

                    sb.Append(_context.GetVariable(name.ToString()));
                }
                else
                {
                    // Lone $ or $<special> — keep the $
                    sb.Append('$');
                    sb.Append(input[i]);
                    i++;
                }
            }
            else
            {
                sb.Append(input[i]);
                i++;
            }
        }

        return sb.ToString();
    }
}
