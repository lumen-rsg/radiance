using System.Text;
using System.Diagnostics;
using Radiance.Builtins;
using Radiance.Interpreter;
using Radiance.Lexer;
using Radiance.Parser;

namespace Radiance.Expansion;

/// <summary>
/// Performs command substitution — executes a command and substitutes its stdout output
/// in place. Supports both <c>$(command)</c> and backtick <c>`command`</c> forms.
/// <para>
/// The output has trailing newlines stripped (BASH behavior).
/// Command substitution is recursive — substitutions within substitutions are expanded.
/// </para>
/// </summary>
public sealed class CommandSubstitution
{
    private readonly ShellContext _context;
    private readonly BuiltinRegistry _builtins;
    private readonly ProcessManager _processManager;
    private readonly Expander? _sharedExpander;

    /// <summary>
    /// Creates a new command substitution expander.
    /// </summary>
    /// <param name="context">The shell execution context.</param>
    /// <param name="builtins">The builtin command registry.</param>
    /// <param name="processManager">The external process manager.</param>
    /// <param name="sharedExpander">Optional shared expander to reuse in nested interpreters.</param>
    public CommandSubstitution(ShellContext context, BuiltinRegistry builtins, ProcessManager processManager, Expander? sharedExpander = null)
    {
        _context = context;
        _builtins = builtins;
        _processManager = processManager;
        _sharedExpander = sharedExpander;
    }

    /// <summary>
    /// Performs command substitution on the given text, replacing all
    /// <c>$(...)</c> and backtick <c>`...`</c> patterns with their output.
    /// </summary>
    /// <param name="text">The text potentially containing command substitutions.</param>
    /// <returns>The text with all command substitutions expanded.</returns>
    public string Expand(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var result = ExpandDollarParen(text);
        result = ExpandBackticks(result);
        return result;
    }

    /// <summary>
    /// Expands <c>$(command)</c> style command substitutions.
    /// Handles nested parentheses correctly.
    /// </summary>
    private string ExpandDollarParen(string text)
    {
        var sb = new StringBuilder();
        var i = 0;

        while (i < text.Length)
        {
            if (text[i] == '$' && i + 1 < text.Length && text[i + 1] == '(' &&
                !(i + 2 < text.Length && text[i + 2] == '('))
            {
                // Found $( — find matching )
                var start = i + 2;
                var depth = 1;
                var j = start;

                while (j < text.Length && depth > 0)
                {
                    if (text[j] == '(')
                        depth++;
                    else if (text[j] == ')')
                        depth--;
                    j++;
                }

                if (depth == 0)
                {
                    var command = text[start..(j - 1)];
                    var output = ExecuteSubstitution(command);
                    sb.Append(output);
                    i = j;
                    continue;
                }
            }

            sb.Append(text[i]);
            i++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Expands backtick <c>`command`</c> style command substitutions.
    /// </summary>
    private string ExpandBackticks(string text)
    {
        var sb = new StringBuilder();
        var i = 0;

        while (i < text.Length)
        {
            if (text[i] == '`')
            {
                // Find closing backtick
                var start = i + 1;
                var j = start;
                var commandText = new StringBuilder();

                while (j < text.Length && text[j] != '`')
                {
                    if (text[j] == '\\' && j + 1 < text.Length)
                    {
                        // Backslash escapes inside backticks: \$ \` \\
                        var next = text[j + 1];
                        if (next is '$' or '`' or '\\')
                        {
                            commandText.Append(next);
                            j += 2;
                            continue;
                        }
                    }

                    commandText.Append(text[j]);
                    j++;
                }

                if (j < text.Length)
                {
                    var output = ExecuteSubstitution(commandText.ToString());
                    sb.Append(output);
                    i = j + 1;
                    continue;
                }
                // No closing backtick — treat as literal
            }

            sb.Append(text[i]);
            i++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Executes a command and captures its stdout output.
    /// Trailing newlines are stripped (BASH behavior).
    /// </summary>
    /// <param name="command">The command string to execute.</param>
    /// <returns>The command's stdout output with trailing newlines stripped.</returns>
    private string ExecuteSubstitution(string command)
    {
        try
        {
            // Run the command through the full Lexer → Parser → Interpreter pipeline
            var lexer = new Lexer.Lexer(command);
            var tokens = lexer.Tokenize();

            var parser = new Radiance.Parser.Parser(tokens);
            var ast = parser.Parse();

            if (ast is null)
                return string.Empty;

            // Capture stdout output
            var captured = new StringWriter();
            var originalOut = Console.Out;

            try
            {
                Console.SetOut(captured);

                // Reuse the shared expander to avoid creating a new object chain per substitution
                var interpreter = _sharedExpander is not null
                    ? new ShellInterpreter(_context, _builtins, _processManager, _sharedExpander)
                    : new ShellInterpreter(_context, _builtins, _processManager);
                _context.LastExitCode = interpreter.Execute(ast);
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            var output = captured.ToString();

            // Strip trailing newlines (BASH behavior)
            return output.TrimEnd('\n', '\r');
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"radiance: command substitution failed: {ex.Message}");
            return string.Empty;
        }
    }
}