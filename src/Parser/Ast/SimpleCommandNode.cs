namespace Radiance.Parser.Ast;

/// <summary>
/// Represents a simple command: a command name with arguments, optional
/// variable assignments (prefixes), and I/O redirections.
/// Example: VAR=1 cmd arg1 arg2 > output.txt
/// </summary>
public sealed record SimpleCommandNode : AstNode
{
    /// <summary>
    /// Variable assignments preceding the command (e.g., VAR=value).
    /// These are set in the environment for the duration of the command only
    /// if a command follows; otherwise they are set in the shell.
    /// </summary>
    public List<AssignmentNode> Assignments { get; init; } = [];

    /// <summary>
    /// The command words (command name + arguments), before expansion.
    /// Each word is a list of <see cref="WordPart"/> segments preserving quoting context.
    /// </summary>
    public List<List<WordPart>> Words { get; init; } = [];

    /// <summary>
    /// I/O redirections attached to this command.
    /// </summary>
    public List<RedirectNode> Redirects { get; init; } = [];

    /// <inheritdoc/>
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitSimpleCommand(this);
}
