namespace Radiance.Parser.Ast;

/// <summary>
/// Represents a shell function definition.
/// BASH syntax: <c>name() { body; }</c> or <c>function name { body; }</c>
/// </summary>
public sealed record FunctionNode : AstNode
{
    /// <summary>
    /// The function name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The function body — a list of commands to execute when the function is called.
    /// </summary>
    public ListNode Body { get; init; } = new();

    /// <inheritdoc/>
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitFunction(this);
}