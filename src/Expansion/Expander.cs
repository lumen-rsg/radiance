using System.Text;
using Radiance.Builtins;
using Radiance.Interpreter;
using Radiance.Parser.Ast;

namespace Radiance.Expansion;

/// <summary>
/// The main expansion engine that orchestrates all expansion phases in the correct order.
/// Implements the BASH expansion order:
/// <list type="number">
/// <item>Brace expansion</item>
/// <item>Tilde expansion</item>
/// <item>Parameter/variable expansion</item>
/// <item>Command substitution</item>
/// <item>Arithmetic expansion</item>
/// <item>Word splitting (handled implicitly)</item>
/// <item>Filename generation (glob expansion)</item>
/// </list>
/// The quoting context of each <see cref="WordPart"/> determines which expansions apply.
/// </summary>
public sealed class Expander
{
    private readonly ShellContext _context;
    private readonly BuiltinRegistry _builtins;
    private readonly ProcessManager _processManager;
    private readonly CommandSubstitution _commandSubstitution;
    private readonly ArithmeticExpander _arithmeticExpander;

    /// <summary>
    /// Creates a new expansion engine.
    /// </summary>
    /// <param name="context">The shell execution context.</param>
    /// <param name="builtins">The builtin command registry.</param>
    /// <param name="processManager">The external process manager.</param>
    public Expander(ShellContext context, BuiltinRegistry builtins, ProcessManager processManager)
    {
        _context = context;
        _builtins = builtins;
        _processManager = processManager;
        _commandSubstitution = new CommandSubstitution(context, builtins, processManager, this);
        _arithmeticExpander = new ArithmeticExpander(context);
    }

    /// <summary>
    /// Expands a list of word parts (representing a single shell word) into one or more
    /// final expanded strings. Glob expansion may split a single word into multiple results.
    /// </summary>
    /// <param name="parts">The word parts composing a single shell word.</param>
    /// <returns>A list of expanded strings (typically one, but glob can produce multiple).</returns>
    public List<string> ExpandWord(List<WordPart> parts, bool skipGlob = false)
    {
        if (parts.Count == 0)
            return [""];

        // Phase 1: Apply quoting-aware expansion to each part, concatenating results
        var sb = new StringBuilder();
        var hasGlobChars = false;
        var firstPartIsUnquotedTilde = false;

        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            var expanded = ExpandPart(part);

            // Check for tilde at start of word (first unquoted part starting with ~)
            if (i == 0 && part.Quoting == WordQuoting.None && expanded.StartsWith('~'))
            {
                firstPartIsUnquotedTilde = true;
            }

            // Track if any unquoted part has glob characters
            if (!skipGlob && !_context.Options.NoGlob && part.Quoting == WordQuoting.None
                && (GlobExpander.ContainsGlobChars(expanded)
                    || (_context.Options.ExtGlob && GlobExpander.ContainsExtGlobChars(expanded))))
            {
                hasGlobChars = true;
            }

            sb.Append(expanded);
        }

        var result = sb.ToString();

        // Phase 2: Tilde expansion (only for unquoted words starting with ~)
        if (firstPartIsUnquotedTilde)
        {
            result = TildeExpander.Expand(result, false, _context);
        }

        // Phase 3: Glob expansion (only for unquoted words with glob characters)
        if (hasGlobChars)
        {
            return GlobExpander.Expand(result, _context.Options);
        }

        return [result];
    }

    /// <summary>
    /// Expands a list of word lists (multiple shell words) into a flat list of
    /// expanded strings. Each input word can produce one or more output strings
    /// (due to glob expansion).
    /// </summary>
    /// <param name="words">The list of word part lists (each representing one shell word).</param>
    /// <returns>A flat list of fully expanded strings.</returns>
    public List<string> ExpandWords(List<List<WordPart>> words)
    {
        var result = new List<string>();

        foreach (var word in words)
        {
            // Phase 0: Brace expansion (before all other expansions)
            // Only applies to unquoted parts
            var braceExpanded = ExpandBraceInWord(word);

            foreach (var braceWord in braceExpanded)
            {
                var expanded = ExpandWord(braceWord);
                result.AddRange(expanded);
            }
        }

        return result;
    }

    /// <summary>
    /// Applies brace expansion to a word's parts.
    /// Only expands unquoted parts that contain { (not preceded by $).
    /// Returns one or more word part lists.
    /// </summary>
    private List<List<WordPart>> ExpandBraceInWord(List<WordPart> parts)
    {
        // Check if any unquoted part contains { that could be brace expansion
        var hasBrace = false;
        foreach (var part in parts)
        {
            if (part.Quoting == WordQuoting.None && part.Text.Contains('{'))
            {
                hasBrace = true;
                break;
            }
        }

        if (!hasBrace)
            return [parts];

        // Reconstruct the raw text for brace expansion
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            if (part.Quoting == WordQuoting.None)
                sb.Append(part.Text);
            else
                return [parts]; // Quoted parts with braces — don't expand
        }

        var rawText = sb.ToString();
        var expanded = BraceExpander.Expand(rawText);

        if (expanded.Count == 1 && expanded[0] == rawText)
            return [parts]; // No expansion happened

        // Convert each expanded string back to a word part list
        var result = new List<List<WordPart>>();
        foreach (var str in expanded)
            result.Add([new WordPart(str)]);

        return result;
    }

    /// <summary>
    /// Expands a single word part according to its quoting context.
    /// </summary>
    private string ExpandPart(WordPart part)
    {
        // Handle process substitution: <(cmd) or >(cmd)
        if (part.ProcessSubstitutionCommand is not null)
        {
            return ExpandProcessSubstitution(part.ProcessSubstitutionCommand, part.IsOutputSubstitution);
        }

        return part.Quoting switch
        {
            // Single-quoted: no expansion at all
            WordQuoting.Single => part.Text,

            // Escaped character: literal
            WordQuoting.Escaped => part.Text,

            // Double-quoted: variable, command substitution, arithmetic (no tilde, no glob)
            WordQuoting.Double => ExpandPartDoubleQuoted(part.Text),

            // Unquoted: all expansions except tilde/glob (handled at word level)
            WordQuoting.None => ExpandPartUnquoted(part.Text),

            _ => part.Text,
        };
    }

    /// <summary>
    /// Expands a process substitution by executing the inner command
    /// and returning the path to a temp file containing the output.
    /// </summary>
    private string ExpandProcessSubstitution(string command, bool isOutput)
    {
        try
        {
            // For input substitution <(cmd): execute command, capture output to temp file
            // For output substitution >(cmd): create temp file for writing
            var tempFile = Path.GetTempFileName();

            if (!isOutput)
            {
                // Execute the command and write output to temp file
                var captured = new System.IO.StringWriter();
                var originalOut = Console.Out;
                Utils.OutputCapture.Push();
                try
                {
                    Console.SetOut(captured);

                    var lexer = new Lexer.Lexer(command);
                    var tokens = lexer.Tokenize();
                    var parser = new Parser.Parser(tokens);
                    var ast = parser.Parse();

                    if (ast is not null)
                    {
                        var interpreter = new ShellInterpreter(_context, _builtins, _processManager, this);
                        _context.LastExitCode = interpreter.Execute(ast);
                    }
                }
                finally
                {
                    Console.SetOut(originalOut);
                    Utils.OutputCapture.Pop();
                }

                var output = captured.ToString().TrimEnd('\n', '\r');
                File.WriteAllText(tempFile, output);
            }

            return tempFile;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"radiance: process substitution failed: {ex.Message}");
            return "/dev/null";
        }
    }

    /// <summary>
    /// Checks if a string contains any expansion characters that require processing.
    /// If none are present, the string can be returned as-is without running through
    /// the full expansion pipeline.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static bool NeedsExpansion(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '$' || c == '`')
                return true;
        }
        return false;
    }

    /// <summary>
    /// Expands a double-quoted part: variable expansion, command substitution, arithmetic.
    /// No tilde or glob expansion.
    /// </summary>
    private string ExpandPartDoubleQuoted(string text)
    {
        // Fast path: skip expansion pipeline for trivial strings
        if (!NeedsExpansion(text))
            return text;

        // Command substitution first (so variables inside $(...) are resolved)
        var result = _commandSubstitution.Expand(text);

        // Arithmetic expansion
        result = _arithmeticExpander.Expand(result);

        // Variable expansion
        result = VariableExpander.Expand(result, _context);

        // Command substitution again (in case variable expansion introduced new ones)
        result = _commandSubstitution.Expand(result);

        return result;
    }

    /// <summary>
    /// Expands an unquoted part: variable expansion, command substitution, arithmetic.
    /// Tilde and glob are handled at the word level in <see cref="ExpandWord"/>.
    /// </summary>
    private string ExpandPartUnquoted(string text)
    {
        // Fast path: skip expansion pipeline for trivial strings
        if (!NeedsExpansion(text))
            return text;

        // Command substitution first
        var result = _commandSubstitution.Expand(text);

        // Arithmetic expansion
        result = _arithmeticExpander.Expand(result);

        // Variable expansion
        result = VariableExpander.Expand(result, _context);

        // Command substitution again (in case variable expansion introduced new ones)
        result = _commandSubstitution.Expand(result);

        return result;
    }

    /// <summary>
    /// Convenience method to expand a simple string with all expansions applied.
    /// Used for contexts like assignment values where there are no quoting concerns.
    /// </summary>
    /// <param name="text">The text to expand.</param>
    /// <returns>The fully expanded string.</returns>
    public string ExpandString(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Apply expansions in order
        var result = _commandSubstitution.Expand(text);
        result = _arithmeticExpander.Expand(result);
        result = VariableExpander.Expand(result, _context);
        result = _commandSubstitution.Expand(result);

        return result;
    }
}