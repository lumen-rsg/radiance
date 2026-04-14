namespace Radiance.Parser.Ast;

/// <summary>
/// Represents a while or until loop.
/// <c>while condition; do body; done</c> — loops while condition succeeds (exit code 0).
/// <c>until condition; do body; done</c> — loops until condition succeeds (exit code 0).
/// </summary>
public sealed record WhileNode : AstNode
{
    /// <summary>
    /// The condition to test before each iteration.
    /// Executed as a command list; exit code 0 = true.
    /// </summary>
    public ListNode Condition { get; init; } = new();

    /// <summary>
    /// The loop body executed for each iteration.
    /// </summary>
    public ListNode Body { get; init; } = new();

    /// <summary>
    /// If true, this is an <c>until</c> loop (loop while condition fails).
    /// If false, this is a <c>while</c> loop (loop while condition succeeds).
    /// </summary>
    public bool IsUntil { get; init; }

    /// <inheritdoc/>
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitWhile(this);
}