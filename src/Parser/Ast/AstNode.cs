namespace Radiance.Parser.Ast;

/// <summary>
/// Abstract base class for all AST nodes in the Radiance shell.
/// Uses the visitor pattern for traversal and interpretation.
/// </summary>
public abstract record AstNode
{
    /// <summary>
    /// Accepts a visitor for pattern-matching dispatch over AST node types.
    /// </summary>
    /// <typeparam name="T">The return type of the visitor.</typeparam>
    /// <param name="visitor">The visitor to dispatch to.</param>
    /// <returns>The result of visiting this node.</returns>
    public abstract T Accept<T>(IAstVisitor<T> visitor);
}
