namespace Radiance.Parser.Ast;

/// <summary>
/// Represents a brace group: <c>{ list; }</c> executed in the current shell scope.
/// Unlike a subshell, variable changes inside a brace group DO affect the parent scope.
/// Example: <c>{ cd /tmp; pwd; }</c> — changes directory in the current shell.
/// </summary>
public sealed record BraceGroupNode : AstNode
{
    /// <summary>
    /// The body of the brace group — a list of commands to execute.
    /// </summary>
    public ListNode Body { get; init; } = new();

    /// <summary>
    /// I/O redirections attached to this brace group.
    /// </summary>
    public List<RedirectNode> Redirects { get; init; } = [];

    /// <inheritdoc/>
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitBraceGroup(this);
}
