namespace Radiance.Parser.Ast;

/// <summary>
/// Represents a for loop: <c>for VAR in words...; do body; done</c>.
/// The iterable words are expanded (including glob) before iteration begins.
/// </summary>
public sealed record ForNode : AstNode
{
    /// <summary>
    /// The loop variable name (e.g., "x" in <c>for x in ...</c>).
    /// </summary>
    public string VariableName { get; init; } = string.Empty;

    /// <summary>
    /// The words to iterate over (before expansion).
    /// Each word is a list of <see cref="WordPart"/> segments.
    /// If empty, iterates over positional parameters ($@).
    /// </summary>
    public List<List<WordPart>> IterableWords { get; init; } = [];

    /// <summary>
    /// The loop body executed for each iteration.
    /// </summary>
    public ListNode Body { get; init; } = new();

    /// <inheritdoc/>
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitFor(this);
}