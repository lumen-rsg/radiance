namespace Radiance.Lexer;

/// <summary>
/// Represents the type of a token produced by the lexer/tokenizer.
/// </summary>
public enum TokenType
{
    /// <summary>A word or identifier (command name, argument, etc.).</summary>
    Word,

    /// <summary>A double-quoted string literal. Allows variable/command expansion.</summary>
    DoubleQuotedString,

    /// <summary>A single-quoted string literal. Everything is literal (no expansion).</summary>
    SingleQuotedString,

    /// <summary>An assignment word (contains '=' before any space, e.g. VAR=value).</summary>
    AssignmentWord,

    /// <summary>A pipe operator: |</summary>
    Pipe,

    /// <summary>Redirect stdout: ></summary>
    GreaterThan,

    /// <summary>Redirect stdin: <</summary>
    LessThan,

    /// <summary>Append stdout: >></summary>
    DoubleGreaterThan,

    /// <summary>Redirect stderr: 2></summary>
    RedirectStderr,

    /// <summary>Duplicate file descriptor redirect: >& (e.g., 2>&1)</summary>
    AmpersandGreaterThan,

    /// <summary>Background operator: &</summary>
    Ampersand,

    /// <summary>Logical AND: &&</summary>
    And,

    /// <summary>Logical OR: ||</summary>
    Or,

    /// <summary>Semicolon: ;</summary>
    Semicolon,

    /// <summary>Double semicolon: ;; (used in case statements)</summary>
    DoubleSemicolon,

    /// <summary>Newline.</summary>
    Newline,

    /// <summary>Left parenthesis: (</summary>
    LParen,

    /// <summary>Right parenthesis: )</summary>
    RParen,

    /// <summary>Left brace: {</summary>
    LBrace,

    /// <summary>Right brace: }</summary>
    RBrace,

    /// <summary>A comment (ignored by the parser).</summary>
    Comment,

    /// <summary>End of input.</summary>
    Eof,
}
