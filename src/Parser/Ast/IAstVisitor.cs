namespace Radiance.Parser.Ast;

/// <summary>
/// Visitor interface for AST node traversal. Implementations can
/// interpret, pretty-print, or analyze the AST.
/// </summary>
/// <typeparam name="T">The return type of visit methods.</typeparam>
public interface IAstVisitor<T>
{
    /// <summary>Visits a simple command node.</summary>
    T VisitSimpleCommand(SimpleCommandNode node);

    /// <summary>Visits a pipeline node.</summary>
    T VisitPipeline(PipelineNode node);

    /// <summary>Visits a list node (command sequences with ;, &&, ||).</summary>
    T VisitList(ListNode node);

    /// <summary>Visits an assignment node.</summary>
    T VisitAssignment(AssignmentNode node);
}
