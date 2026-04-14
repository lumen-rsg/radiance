namespace Radiance.Parser.Ast;

/// <summary>
/// Represents a pipeline of commands connected by pipes (|).
/// Example: cmd1 | cmd2 | cmd3
/// </summary>
public sealed record PipelineNode : AstNode
{
    /// <summary>
    /// The commands in the pipeline, in order from left to right.
    /// A single command with no pipes is a pipeline of one element.
    /// </summary>
    public List<SimpleCommandNode> Commands { get; init; } = [];

    /// <inheritdoc/>
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitPipeline(this);
}
