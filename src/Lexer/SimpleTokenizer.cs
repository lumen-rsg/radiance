using System.Text;

namespace Radiance.Lexer;

/// <summary>
/// A simple tokenizer that splits input into words, respecting single and double quotes,
/// and recognizes basic shell operators (|, >, <, >>, &, &&, ||, ;).
/// </summary>
public static class SimpleTokenizer
{
    /// <summary>
    /// Tokenizes the given input string into a list of tokens.
    /// </summary>
    /// <param name="input">The raw shell input line.</param>
    /// <returns>A list of tokens parsed from the input.</returns>
    public static List<Token> Tokenize(string input)
    {
        var tokens = new List<Token>();
        var i = 0;

        while (i < input.Length)
        {
            // Skip whitespace
            if (char.IsWhiteSpace(input[i]))
            {
                i++;
                continue;
            }

            // Single-quoted string — everything is literal until closing '
            if (input[i] == '\'')
            {
                var sb = new StringBuilder();
                i++; // skip opening quote
                while (i < input.Length && input[i] != '\'')
                {
                    sb.Append(input[i]);
                    i++;
                }

                if (i < input.Length) i++; // skip closing quote
                tokens.Add(new Token(TokenType.String, sb.ToString()));
                continue;
            }

            // Double-quoted string — allows $ expansion (handled later in expansion phase)
            if (input[i] == '"')
            {
                var sb = new StringBuilder();
                i++; // skip opening quote
                while (i < input.Length && input[i] != '"')
                {
                    if (input[i] == '\\' && i + 1 < input.Length)
                    {
                        var next = input[i + 1];
                        if (next is '"' or '\\' or '$' or '`')
                        {
                            sb.Append(next);
                            i += 2;
                            continue;
                        }
                    }

                    sb.Append(input[i]);
                    i++;
                }

                if (i < input.Length) i++; // skip closing quote
                tokens.Add(new Token(TokenType.String, sb.ToString()));
                continue;
            }

            // Operators
            if (input[i] == '|')
            {
                if (i + 1 < input.Length && input[i + 1] == '|')
                {
                    tokens.Add(new Token(TokenType.Or, "||"));
                    i += 2;
                }
                else
                {
                    tokens.Add(new Token(TokenType.Pipe, "|"));
                    i++;
                }

                continue;
            }

            if (input[i] == '&')
            {
                if (i + 1 < input.Length && input[i + 1] == '&')
                {
                    tokens.Add(new Token(TokenType.And, "&&"));
                    i += 2;
                }
                else
                {
                    tokens.Add(new Token(TokenType.Ampersand, "&"));
                    i++;
                }

                continue;
            }

            if (input[i] == ';')
            {
                tokens.Add(new Token(TokenType.Semicolon, ";"));
                i++;
                continue;
            }

            if (input[i] == '>')
            {
                if (i + 1 < input.Length && input[i + 1] == '>')
                {
                    tokens.Add(new Token(TokenType.DoubleGreaterThan, ">>"));
                    i += 2;
                }
                else
                {
                    tokens.Add(new Token(TokenType.GreaterThan, ">"));
                    i++;
                }

                continue;
            }

            if (input[i] == '<')
            {
                tokens.Add(new Token(TokenType.LessThan, "<"));
                i++;
                continue;
            }

            // Word — unquoted sequence of non-special characters
            var word = new StringBuilder();
            while (i < input.Length && !char.IsWhiteSpace(input[i]) && !IsSpecial(input[i]))
            {
                if (input[i] == '\\' && i + 1 < input.Length)
                {
                    // Backslash escape — take next char literally
                    word.Append(input[i + 1]);
                    i += 2;
                }
                else
                {
                    word.Append(input[i]);
                    i++;
                }
            }

            if (word.Length > 0)
            {
                tokens.Add(new Token(TokenType.Word, word.ToString()));
            }
        }

        tokens.Add(new Token(TokenType.Eof, string.Empty));
        return tokens;
    }

    /// <summary>
    /// Splits tokens into separate command lists by semicolons, returning
    /// groups of tokens that represent individual commands.
    /// </summary>
    /// <param name="tokens">The full token list (including EOF).</param>
    /// <returns>A list of command token groups (each group excludes the semicolon/EOF separator).</returns>
    public static List<List<Token>> SplitCommands(List<Token> tokens)
    {
        var commands = new List<List<Token>>();
        var current = new List<Token>();

        foreach (var token in tokens)
        {
            if (token.Type is TokenType.Semicolon or TokenType.Eof)
            {
                if (current.Count > 0)
                {
                    commands.Add(current);
                    current = new List<Token>();
                }
            }
            else
            {
                current.Add(token);
            }
        }

        return commands;
    }

    /// <summary>
    /// Checks whether a character is a special shell operator character.
    /// </summary>
    private static bool IsSpecial(char c) =>
        c is '|' or '&' or ';' or '>' or '<' or '\'' or '"';
}