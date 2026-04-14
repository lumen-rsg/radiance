namespace Radiance.Parser.Ast;

/// <summary>
/// Represents a list of pipelines connected by sequential (;, newline),
/// logical AND (&&), or logical OR (||) operators.
/// This is the top-level AST node produced by the parser.
/// </summary>
public sealed record ListNode : AstNode
{
    /// <summary>
    /// The pipelines in this list, in execution order.
    /// </summary>
    public List<PipelineNode> Pipelines { get; init; } = [];

    /// <summary>
    /// The separators between pipelines. The list has Count-1 elements
    /// (or Count elements if there's a trailing separator).
    /// Each separator is the token type of the operator: Semicolon, Newline, And, Or, Ampersand.
    /// </summary>
    public List<Radiance.Lexer.TokenType> Separators { get; init; } = [];

    /// <inheritdoc/>
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitList(this);
}
