namespace Radiance.Parser.Ast;

/// <summary>
/// Represents a single case item (pattern(s) + body) within a <see cref="CaseNode"/>.
/// </summary>
/// <param name="Patterns">The glob patterns to match against (before expansion).</param>
/// <param name="Body">The command list to execute if any pattern matches.</param>
public sealed record CaseItem(List<List<WordPart>> Patterns, ListNode Body);

/// <summary>
/// Represents a case statement: <c>case WORD in pattern) body ;; ... esac</c>.
/// The word is expanded and matched against each case item's patterns using glob-style matching.
/// </summary>
public sealed record CaseNode : AstNode
{
    /// <summary>
    /// The word to match against patterns (before expansion).
    /// </summary>
    public List<WordPart> Word { get; init; } = [];

    /// <summary>
    /// The case items, each with one or more patterns and a body.
    /// </summary>
    public List<CaseItem> Items { get; init; } = [];

    /// <inheritdoc/>
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitCase(this);
}