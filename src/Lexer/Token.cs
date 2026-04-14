namespace Radiance.Lexer;

/// <summary>
/// Represents a single token produced by the lexer, including source position.
/// </summary>
/// <param name="Type">The type of the token.</param>
/// <param name="Value">The raw string value of the token.</param>
/// <param name="Line">The 1-based line number where the token starts.</param>
/// <param name="Column">The 1-based column number where the token starts.</param>
public sealed record Token(TokenType Type, string Value, int Line = 1, int Column = 1, bool HasLeadingWhitespace = false)
{
    /// <summary>
    /// Returns a string representation of the token for debugging.
    /// </summary>
    public override string ToString() => $"{Type}: \"{Value}\" (line {Line}, col {Column}, ws={HasLeadingWhitespace})";
}
