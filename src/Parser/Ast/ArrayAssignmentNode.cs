namespace Radiance.Parser.Ast;

/// <summary>
/// Represents an array variable assignment: <c>VAR=(val1 val2 val3)</c>.
/// The elements are raw strings that will be expanded at interpretation time.
/// </summary>
public sealed record ArrayAssignmentNode : AstNode
{
    /// <summary>
    /// The variable name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The array element values (raw, before expansion).
    /// </summary>
    public List<string> Elements { get; init; } = [];

    /// <inheritdoc/>
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitArrayAssignment(this);
}
