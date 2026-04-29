namespace Radiance.Parser.Ast;

/// <summary>
/// Represents a segment of a word with its quoting context.
/// Used to determine which expansions should be applied during the expansion phase.
/// </summary>
/// <param name="Text">The text content of this word segment.</param>
/// <param name="Quoting">The quoting type of this segment.</param>
public sealed record WordPart(string Text, WordQuoting Quoting = WordQuoting.None)
{
    /// <summary>
    /// For process substitution: the inner command to execute.
    /// Null for normal word parts.
    /// </summary>
    public string? ProcessSubstitutionCommand { get; init; }

    /// <summary>
    /// True for >(cmd) output substitution, false for &lt;(cmd) input substitution.
    /// </summary>
    public bool IsOutputSubstitution { get; init; }
}

/// <summary>
/// Specifies the quoting context of a word segment, which determines
/// which expansions are applied.
/// </summary>
public enum WordQuoting
{
    /// <summary>No quoting — all expansions apply (variable, command substitution, glob, tilde).</summary>
    None,

    /// <summary>Double quotes — variable expansion, command substitution, arithmetic apply; glob and tilde do not.</summary>
    Double,

    /// <summary>Single quotes — no expansions apply; everything is literal.</summary>
    Single,

    /// <summary>Backslash-escaped character — literal, no expansion.</summary>
    Escaped,
}