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

    /// <summary>Visits an if/elif/else conditional node.</summary>
    T VisitIf(IfNode node);

    /// <summary>Visits a for loop node.</summary>
    T VisitFor(ForNode node);

    /// <summary>Visits a while/until loop node.</summary>
    T VisitWhile(WhileNode node);

    /// <summary>Visits a case statement node.</summary>
    T VisitCase(CaseNode node);

    /// <summary>Visits a function definition node.</summary>
    T VisitFunction(FunctionNode node);
}
