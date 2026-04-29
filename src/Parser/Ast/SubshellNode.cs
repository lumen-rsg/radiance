namespace Radiance.Parser.Ast;

/// <summary>
/// Represents a subshell: a parenthesized command group executed in a child context.
/// Variable changes inside a subshell do not affect the parent shell.
/// Example: <c>(cd /tmp; pwd)</c> — changes directory only in the subshell.
/// </summary>
public sealed record SubshellNode : AstNode
{
    /// <summary>
    /// The body of the subshell — a list of commands to execute.
    /// </summary>
    public ListNode Body { get; init; } = new();

    /// <summary>
    /// I/O redirections attached to this subshell.
    /// </summary>
    public List<RedirectNode> Redirects { get; init; } = [];

    /// <inheritdoc/>
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitSubshell(this);
}
