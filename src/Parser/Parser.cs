using Radiance.Lexer;
using Radiance.Parser.Ast;

// ReSharper disable once CheckNamespace
namespace Radiance.Parser;

/// <summary>
/// A recursive-descent parser that converts a flat token stream into an AST.
/// 
/// Grammar (simplified BASH-like):
/// <code>
/// list       := and_or ( (';' | '&' | '\n') and_or )* [ ';' | '&' | '\n' ]
/// and_or     := pipeline ( ('&&' | '||') pipeline )*
/// pipeline   := simple_command ( '|' simple_command )*
/// simple_command := (assignment)* [ word+ ] (redirect)*
/// assignment := ASSIGNMENT_WORD
/// redirect   := ('>' | '>>' | '<') word
/// </code>
/// </summary>
public sealed class Parser
{
    private readonly List<Token> _tokens;
    private int _pos;

    /// <summary>
    /// Creates a parser over the given token list.
    /// </summary>
    /// <param name="tokens">The tokens from the lexer (must include EOF).</param>
    public Parser(List<Token> tokens)
    {
        _tokens = tokens;
        _pos = 0;
    }

    /// <summary>
    /// Parses the token stream and returns the top-level AST node (a list).
    /// Returns null if the input is empty or only contains comments/whitespace.
    /// </summary>
    /// <returns>The parsed <see cref="ListNode"/>, or null if empty input.</returns>
    public ListNode? Parse()
    {
        SkipCommentsAndNewlines();

        if (IsAtEnd())
            return null;

        return ParseList();
    }

    /// <summary>
    /// Parses a list: pipelines separated by ;, &, newlines, &&, ||.
    /// </summary>
    private ListNode ParseList()
    {
        var pipelines = new List<PipelineNode>();
        var separators = new List<TokenType>();

        pipelines.Add(ParseAndOr());

        while (IsListSeparator(out var separatorType))
        {
            separators.Add(separatorType);
            Advance(); // consume the separator
            SkipCommentsAndNewlines();

            // A trailing separator doesn't start a new pipeline
            if (IsAtEnd() || IsListSeparatorPeek())
                break;

            pipelines.Add(ParseAndOr());
        }

        return new ListNode { Pipelines = pipelines, Separators = separators };
    }

    /// <summary>
    /// Parses a single pipeline. The && and || operators are handled at the
    /// <see cref="ParseList"/> level, so this just delegates to <see cref="ParsePipeline"/>.
    /// </summary>
    private PipelineNode ParseAndOr()
    {
        return ParsePipeline();
    }

    /// <summary>
    /// Parses a pipeline: simple commands connected by |.
    /// For Phase 2, multi-command pipelines are parsed but executed as single commands.
    /// Full pipe execution will be implemented in Phase 3.
    /// </summary>
    private PipelineNode ParsePipeline()
    {
        var commands = new List<SimpleCommandNode> { ParseSimpleCommand() };

        // Collect piped commands (execution deferred to Phase 3)
        while (Current().Type == TokenType.Pipe)
        {
            Advance(); // consume |
            SkipCommentsAndNewlines();
            commands.Add(ParseSimpleCommand());
        }

        return new PipelineNode { Commands = commands };
    }

    /// <summary>
    /// Parses a simple command: optional assignments, command words, and redirections.
    /// Words are stored as <see cref="WordPart"/> lists to preserve quoting context
    /// for the expansion phase.
    /// </summary>
    private SimpleCommandNode ParseSimpleCommand()
    {
        var assignments = new List<AssignmentNode>();
        var words = new List<List<WordPart>>();
        var redirects = new List<RedirectNode>();

        SkipCommentsAndNewlines();

        // Collect prefix assignments
        while (Current().Type == TokenType.AssignmentWord)
        {
            var token = Advance();
            var eqIdx = token.Value.IndexOf('=');
            var name = token.Value[..eqIdx];
            var value = token.Value[(eqIdx + 1)..];
            assignments.Add(new AssignmentNode(name, value));
        }

        // Collect command words (Word, DoubleQuotedString, or SingleQuotedString tokens)
        // Adjacent quoted/unquoted segments are merged into a single word (e.g., hello"world")
        while (IsWordToken())
        {
            var wordParts = new List<WordPart>();

            // Collect adjacent word-like tokens into a single word
            while (IsWordToken())
            {
                var token = Current();
                switch (token.Type)
                {
                    case TokenType.Word:
                        wordParts.Add(new WordPart(token.Value, WordQuoting.None));
                        break;
                    case TokenType.DoubleQuotedString:
                        wordParts.Add(new WordPart(token.Value, WordQuoting.Double));
                        break;
                    case TokenType.SingleQuotedString:
                        wordParts.Add(new WordPart(token.Value, WordQuoting.Single));
                        break;
                }

                Advance();
            }

            words.Add(wordParts);
        }

        // Collect redirections
        while (IsRedirectOperator())
        {
            var redirect = ParseRedirect();
            if (redirect is not null)
                redirects.Add(redirect);
        }

        return new SimpleCommandNode
        {
            Assignments = assignments,
            Words = words,
            Redirects = redirects
        };
    }

    /// <summary>
    /// Checks if the current token is a word-like token (Word, DoubleQuotedString, or SingleQuotedString).
    /// </summary>
    private bool IsWordToken() =>
        Current().Type is TokenType.Word or TokenType.DoubleQuotedString or TokenType.SingleQuotedString;

    /// <summary>
    /// Parses a single I/O redirection.
    /// </summary>
    private RedirectNode? ParseRedirect()
    {
        var opToken = Advance();
        var targetToken = Current();

        if (!IsWordToken())
        {
            ReportError($"expected filename after redirection operator '{opToken.Value}', got {targetToken.Type}");
            return null;
        }

        // Build word parts for the redirect target (supports quoting)
        var targetParts = new List<WordPart>();
        while (IsWordToken())
        {
            var token = Current();
            switch (token.Type)
            {
                case TokenType.Word:
                    targetParts.Add(new WordPart(token.Value, WordQuoting.None));
                    break;
                case TokenType.DoubleQuotedString:
                    targetParts.Add(new WordPart(token.Value, WordQuoting.Double));
                    break;
                case TokenType.SingleQuotedString:
                    targetParts.Add(new WordPart(token.Value, WordQuoting.Single));
                    break;
            }

            Advance();
        }

        var fd = opToken.Type switch
        {
            TokenType.LessThan => 0,
            _ => 1
        };

        return new RedirectNode(opToken.Type, targetParts, fd);
    }

    // ──── Helper Methods ────

    /// <summary>
    /// Checks if the current token is a list-level separator (;, &, newline, &&, ||).
    /// </summary>
    private bool IsListSeparator(out TokenType separatorType)
    {
        var type = Current().Type;
        if (type is TokenType.Semicolon or TokenType.Newline or TokenType.Ampersand)
        {
            separatorType = type;
            return true;
        }

        // && and || are also list-level separators for the outer list
        if (type is TokenType.And or TokenType.Or)
        {
            separatorType = type;
            return true;
        }

        separatorType = default;
        return false;
    }

    /// <summary>
    /// Checks if the next token (after current) is a list separator without consuming.
    /// Used to detect trailing separators.
    /// </summary>
    private bool IsListSeparatorPeek()
    {
        var type = Current().Type;
        return type is TokenType.Semicolon or TokenType.Newline or TokenType.Ampersand
            or TokenType.And or TokenType.Or;
    }

    /// <summary>
    /// Checks if the current token is a redirection operator.
    /// </summary>
    private bool IsRedirectOperator() =>
        Current().Type is TokenType.GreaterThan or TokenType.DoubleGreaterThan
            or TokenType.LessThan;

    /// <summary>
    /// Returns the current token without advancing.
    /// </summary>
    private Token Current() =>
        _pos < _tokens.Count ? _tokens[_pos] : _tokens[^1]; // return EOF if past end

    /// <summary>
    /// Returns the current token and advances the position.
    /// </summary>
    private Token Advance()
    {
        var token = Current();
        if (!IsAtEnd())
            _pos++;
        return token;
    }

    /// <summary>
    /// Checks if we've reached the end of the token stream (EOF).
    /// </summary>
    private bool IsAtEnd() => Current().Type == TokenType.Eof;

    /// <summary>
    /// Skips over comments and newlines.
    /// </summary>
    private void SkipCommentsAndNewlines()
    {
        while (Current().Type is TokenType.Comment or TokenType.Newline)
        {
            Advance();
        }
    }

    /// <summary>
    /// Reports a parse error to stderr.
    /// </summary>
    private static void ReportError(string message)
    {
        Console.Error.WriteLine($"radiance: parse error: {message}");
    }
}