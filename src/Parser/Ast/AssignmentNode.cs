namespace Radiance.Parser.Ast;

/// <summary>
/// Represents a variable assignment (e.g., VAR=value).
/// Assignments can appear as standalone statements or as prefixes to commands.
/// </summary>
/// <param name="Name">The variable name (left side of '=').</param>
/// <param name="Value">The variable value (right side of '='), before expansion.</param>
public sealed record AssignmentNode(string Name, string Value) : AstNode
{
    /// <inheritdoc/>
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitAssignment(this);
}
