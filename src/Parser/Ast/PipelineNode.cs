namespace Radiance.Parser.Ast;

/// <summary>
/// Represents a pipeline of commands connected by pipes (|).
/// Example: cmd1 | cmd2 | cmd3
/// Each element can be a <see cref="SimpleCommandNode"/> or a compound command
/// (<see cref="IfNode"/>, <see cref="ForNode"/>, <see cref="WhileNode"/>, <see cref="CaseNode"/>).
/// </summary>
public sealed record PipelineNode : AstNode
{
    /// <summary>
    /// The commands in the pipeline, in order from left to right.
    /// A single command with no pipes is a pipeline of one element.
    /// Each element is an <see cref="AstNode"/> (typically <see cref="SimpleCommandNode"/>).
    /// </summary>
    public List<AstNode> Commands { get; init; } = [];

    /// <inheritdoc/>
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitPipeline(this);
}
