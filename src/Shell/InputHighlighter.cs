using Radiance.Lexer;

namespace Radiance.Shell;

/// <summary>
/// Provides real-time syntax highlighting for shell input lines.
/// Uses the SimpleTokenizer to tokenize input and applies ANSI colors
/// based on token type: keywords (cyan), strings (green), variables (yellow),
/// comments (gray), operators (magenta), commands (white bold).
/// </summary>
public static class InputHighlighter
{
    // ANSI color codes
    private const string Reset = "\x1b[0m";
    private const string Bold = "\x1b[1m";
    private const string Cyan = "\x1b[36m";
    private const string Green = "\x1b[32m";
    private const string Yellow = "\x1b[33m";
    private const string Gray = "\x1b[90m";
    private const string Magenta = "\x1b[35m";
    private const string White = "\x1b[37m";

    /// <summary>
    /// Shell keywords that should be highlighted specially.
    /// </summary>
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "if", "then", "elif", "else", "fi",
        "for", "in", "do", "done",
        "while", "until",
        "case", "esac",
        "function", "select",
        "time"
    };

    /// <summary>
    /// Highlights the input string with ANSI color codes based on token types.
    /// Returns the highlighted string (longer than input due to ANSI codes).
    /// </summary>
    public static string Highlight(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        try
        {
            var tokens = SimpleTokenizer.Tokenize(input);
            var result = new System.Text.StringBuilder(input.Length * 2);
            var inputPos = 0;

            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];

                if (token.Type == TokenType.Eof)
                    break;

                // Find the token in the original input to preserve whitespace
                var tokenValue = token.Value;
                var tokenIdx = input.IndexOf(tokenValue, inputPos, StringComparison.Ordinal);
                if (tokenIdx < 0)
                {
                    // Fallback: append remaining
                    result.Append(input[inputPos..]);
                    break;
                }

                // Append whitespace/comments between tokens
                if (tokenIdx > inputPos)
                    result.Append(input[inputPos..tokenIdx]);

                // Apply color based on token type
                result.Append(GetColoredToken(token, i == 0));

                inputPos = tokenIdx + tokenValue.Length;
            }

            // Append any remaining text
            if (inputPos < input.Length)
                result.Append(input[inputPos..]);

            return result.ToString();
        }
        catch
        {
            return input;
        }
    }

    /// <summary>
    /// Returns a colored version of the token.
    /// </summary>
    private static string GetColoredToken(Token token, bool isFirstWord)
    {
        return token.Type switch
        {
            TokenType.Word when Keywords.Contains(token.Value) =>
                $"{Bold}{Cyan}{token.Value}{Reset}",

            TokenType.Word when token.Value.StartsWith('$') =>
                $"{Yellow}{token.Value}{Reset}",

            TokenType.Word when isFirstWord =>
                $"{Bold}{White}{token.Value}{Reset}",

            TokenType.DoubleQuotedString =>
                $"{Green}\"{token.Value}\"{Reset}",

            TokenType.SingleQuotedString =>
                $"{Green}'{token.Value}'{Reset}",

            TokenType.Pipe or TokenType.And or TokenType.Or or TokenType.Semicolon =>
                $"{Magenta}{token.Value}{Reset}",

            _ => token.Value
        };
    }

    /// <summary>
    /// Returns the visible length of a string (excluding ANSI escape sequences).
    /// Used for cursor positioning.
    /// </summary>
    public static int VisibleLength(string text)
    {
        var len = 0;
        var inEscape = false;
        foreach (var c in text)
        {
            if (c == '\x1b')
            {
                inEscape = true;
                continue;
            }
            if (inEscape)
            {
                if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z')
                    inEscape = false;
                continue;
            }
            len++;
        }
        return len;
    }
}
