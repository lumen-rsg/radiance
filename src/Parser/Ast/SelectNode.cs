namespace Radiance.Parser.Ast;

/// <summary>
/// Represents a select loop: <c>select VAR in words...; do body; done</c>.
/// Displays a numbered menu, reads user selection, sets the variable, and executes the body.
/// </summary>
public sealed record SelectNode : AstNode
{
    /// <summary>
    /// The loop variable name (e.g., "x" in <c>select x in ...</c>).
    /// Set to the selected menu item value on each iteration.
    /// </summary>
    public string VariableName { get; init; } = string.Empty;

    /// <summary>
    /// The words to display as menu items (before expansion).
    /// If empty, uses positional parameters ($@).
    /// </summary>
    public List<List<WordPart>> IterableWords { get; init; } = [];

    /// <summary>
    /// The loop body executed for each selection.
    /// </summary>
    public ListNode Body { get; init; } = new();

    /// <inheritdoc/>
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitSelect(this);
}
