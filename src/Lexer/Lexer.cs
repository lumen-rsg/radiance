using System.Text;

namespace Radiance.Lexer;

/// <summary>
/// A proper shell lexer that converts raw input into a stream of tokens
/// with position tracking (line, column). Handles quoting, comments,
/// operators, and assignment detection.
/// </summary>
public sealed class Lexer
{
    private readonly string _source;
    private int _pos;
    private int _line;
    private int _column;

    /// <summary>
    /// Creates a new lexer for the given input string.
    /// </summary>
    /// <param name="source">The raw shell input to tokenize.</param>
    public Lexer(string source)
    {
        _source = source;
        _pos = 0;
        _line = 1;
        _column = 1;
    }

    /// <summary>
    /// Tokenizes the entire input and returns all tokens (including EOF).
    /// </summary>
    /// <returns>A list of tokens.</returns>
    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();

        while (true)
        {
            var token = NextToken();
            tokens.Add(token);

            if (token.Type == TokenType.Eof)
                break;
        }

        return tokens;
    }

    /// <summary>
    /// Reads and returns the next token from the input.
    /// </summary>
    private Token NextToken()
    {
        SkipWhitespace();

        if (_pos >= _source.Length)
            return MakeToken(TokenType.Eof, string.Empty);

        var c = _source[_pos];

        // Comment — skip to end of line
        if (c == '#')
        {
            var start = _pos;
            while (_pos < _source.Length && _source[_pos] != '\n')
            {
                Advance();
            }

            return MakeToken(TokenType.Comment, _source[start.._pos]);
        }

        // Newline
        if (c == '\n')
        {
            var token = MakeToken(TokenType.Newline, "\n");
            Advance();
            _line++;
            _column = 1;
            return token;
        }

        // Single-quoted string — everything is literal until closing '
        if (c == '\'')
        {
            return ReadSingleQuotedString();
        }

        // Double-quoted string — allows $ expansion (handled later in expansion phase)
        if (c == '"')
        {
            return ReadDoubleQuotedString();
        }

        // Operators — multi-char first
        if (c == '|')
        {
            if (Peek(1) == '|')
            {
                var token = MakeToken(TokenType.Or, "||");
                Advance(); Advance();
                return token;
            }

            {
                var token = MakeToken(TokenType.Pipe, "|");
                Advance();
                return token;
            }
        }

        if (c == '&')
        {
            if (Peek(1) == '&')
            {
                var token = MakeToken(TokenType.And, "&&");
                Advance(); Advance();
                return token;
            }

            {
                var token = MakeToken(TokenType.Ampersand, "&");
                Advance();
                return token;
            }
        }

        if (c == ';')
        {
            var token = MakeToken(TokenType.Semicolon, ";");
            Advance();
            return token;
        }

        if (c == '>')
        {
            if (Peek(1) == '>')
            {
                var token = MakeToken(TokenType.DoubleGreaterThan, ">>");
                Advance(); Advance();
                return token;
            }

            {
                var token = MakeToken(TokenType.GreaterThan, ">");
                Advance();
                return token;
            }
        }

        if (c == '<')
        {
            var token = MakeToken(TokenType.LessThan, "<");
            Advance();
            return token;
        }

        if (c == '(')
        {
            var token = MakeToken(TokenType.LParen, "(");
            Advance();
            return token;
        }

        if (c == ')')
        {
            var token = MakeToken(TokenType.RParen, ")");
            Advance();
            return token;
        }

        // Word — unquoted sequence of non-special characters
        return ReadWord();
    }

    /// <summary>
    /// Reads a single-quoted string. Everything between the quotes is literal.
    /// </summary>
    private Token ReadSingleQuotedString()
    {
        var startLine = _line;
        var startCol = _column;
        Advance(); // skip opening '

        var sb = new StringBuilder();

        while (_pos < _source.Length && _source[_pos] != '\'')
        {
            sb.Append(_source[_pos]);
            if (_source[_pos] == '\n')
            {
                _line++;
                _column = 1;
            }
            else
            {
                _column++;
            }

            _pos++;
        }

        if (_pos < _source.Length)
            Advance(); // skip closing '

        return new Token(TokenType.String, sb.ToString(), startLine, startCol);
    }

    /// <summary>
    /// Reads a double-quoted string. Supports backslash escapes for " \ $ `.
    /// Variable expansion is handled in the expansion phase, not the lexer.
    /// </summary>
    private Token ReadDoubleQuotedString()
    {
        var startLine = _line;
        var startCol = _column;
        Advance(); // skip opening "

        var sb = new StringBuilder();

        while (_pos < _source.Length && _source[_pos] != '"')
        {
            if (_source[_pos] == '\\' && _pos + 1 < _source.Length)
            {
                var next = _source[_pos + 1];
                if (next is '"' or '\\' or '$' or '`' or '\n')
                {
                    if (next == '\n')
                    {
                        // Line continuation inside double quotes
                        Advance(); // skip backslash
                        Advance(); // skip newline
                        _line++;
                        _column = 1;
                        continue;
                    }

                    sb.Append(next);
                    Advance(); Advance();
                    continue;
                }
            }

            sb.Append(_source[_pos]);
            if (_source[_pos] == '\n')
            {
                _line++;
                _column = 1;
            }
            else
            {
                _column++;
            }

            _pos++;
        }

        if (_pos < _source.Length)
            Advance(); // skip closing "

        return new Token(TokenType.String, sb.ToString(), startLine, startCol);
    }

    /// <summary>
    /// Reads an unquoted word. Handles backslash escapes and detects
    /// assignment words (VAR=value pattern).
    /// </summary>
    private Token ReadWord()
    {
        var startLine = _line;
        var startCol = _column;
        var sb = new StringBuilder();

        while (_pos < _source.Length && !IsWordTerminator(_source[_pos]))
        {
            if (_source[_pos] == '\\' && _pos + 1 < _source.Length)
            {
                // Backslash escape — take next char literally
                Advance(); // skip backslash
                sb.Append(_source[_pos]);
                Advance();
                continue;
            }

            sb.Append(_source[_pos]);
            Advance();
        }

        var value = sb.ToString();

        // Detect assignment words: must start with a valid identifier (letter/_),
        // contain '=', and the '=' must not be the first character.
        var tokenType = IsAssignmentWord(value) ? TokenType.AssignmentWord : TokenType.Word;

        return new Token(tokenType, value, startLine, startCol);
    }

    /// <summary>
    /// Checks whether a character terminates an unquoted word.
    /// </summary>
    private static bool IsWordTerminator(char c) =>
        char.IsWhiteSpace(c) || c is '|' or '&' or ';' or '>' or '<' or '(' or ')' or '\'' or '"' or '\n' or '#';

    /// <summary>
    /// Determines if the word value looks like an assignment (e.g. VAR=value).
    /// A valid assignment starts with a letter or underscore, followed by
    /// letters/digits/underscores, then an '=' character.
    /// </summary>
    private static bool IsAssignmentWord(string value)
    {
        var eqIdx = value.IndexOf('=');
        if (eqIdx <= 0)
            return false;

        // The part before '=' must be a valid identifier
        for (var i = 0; i < eqIdx; i++)
        {
            var c = value[i];
            if (i == 0)
            {
                if (!char.IsLetter(c) && c != '_')
                    return false;
            }
            else
            {
                if (!char.IsLetterOrDigit(c) && c != '_')
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Skips whitespace (spaces and tabs), but not newlines.
    /// </summary>
    private void SkipWhitespace()
    {
        while (_pos < _source.Length && (_source[_pos] == ' ' || _source[_pos] == '\t'))
        {
            Advance();
        }
    }

    /// <summary>
    /// Advances the position by one character, updating line/column tracking.
    /// </summary>
    private void Advance()
    {
        if (_pos < _source.Length)
        {
            _pos++;
            _column++;
        }
    }

    /// <summary>
    /// Peeks at a character at the given offset from the current position.
    /// Returns '\0' if out of bounds.
    /// </summary>
    private char Peek(int offset = 0)
    {
        var idx = _pos + offset;
        return idx < _source.Length ? _source[idx] : '\0';
    }

    /// <summary>
    /// Creates a token at the current position.
    /// </summary>
    private Token MakeToken(TokenType type, string value)
    {
        return new Token(type, value, _line, _column);
    }
}
