namespace Radiance.Parser.Ast;

/// <summary>
/// Represents an if/elif/else/fi conditional construct.
/// The condition is a <see cref="ListNode"/> whose exit code determines the branch.
/// </summary>
public sealed record IfNode : AstNode
{
    /// <summary>
    /// The condition to test (executed as a command list; exit code 0 = true).
    /// </summary>
    public ListNode Condition { get; init; } = new();

    /// <summary>
    /// The body to execute if the condition succeeds (exit code 0).
    /// </summary>
    public ListNode ThenBody { get; init; } = new();

    /// <summary>
    /// Optional elif branches. Each tuple is (condition, then-body).
    /// </summary>
    public List<(ListNode Condition, ListNode Body)> ElifBranches { get; init; } = [];

    /// <summary>
    /// Optional else body, executed when no condition matches.
    /// </summary>
    public ListNode? ElseBody { get; init; }

    /// <inheritdoc/>
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitIf(this);
}