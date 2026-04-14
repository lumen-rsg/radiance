using Radiance.Lexer;
using Radiance.Parser;
using Radiance.Parser.Ast;

namespace Radiance.Tests;

public sealed class ParserTests
{
    private static ListNode? Parse(string input)
    {
        var tokens = new Radiance.Lexer.Lexer(input).Tokenize();
        return new Radiance.Parser.Parser(tokens).Parse();
    }

    // ──── Simple Commands ────

    [Fact]
    public void SimpleCommand_SingleWord()
    {
        var ast = Parse("echo");
        Assert.NotNull(ast);
        var cmd = Assert.IsType<PipelineNode>(ast.Pipelines[0]);
        var simple = Assert.IsType<SimpleCommandNode>(cmd.Commands[0]);
        Assert.Single(simple.Words);
        Assert.Equal("echo", simple.Words[0][0].Text);
    }

    [Fact]
    public void SimpleCommand_WithArgs()
    {
        var ast = Parse("echo hello world");
        Assert.NotNull(ast);
        var cmd = Assert.IsType<PipelineNode>(ast.Pipelines[0]);
        var simple = Assert.IsType<SimpleCommandNode>(cmd.Commands[0]);
        Assert.Equal(3, simple.Words.Count);
    }

    [Fact]
    public void EmptyInput_ReturnsNull()
    {
        var ast = Parse("");
        Assert.Null(ast);
    }

    // ──── Pipelines ────

    [Fact]
    public void Pipeline_TwoCommands()
    {
        var ast = Parse("echo hello | cat");
        Assert.NotNull(ast);
        var pipeline = Assert.IsType<PipelineNode>(ast.Pipelines[0]);
        Assert.Equal(2, pipeline.Commands.Count);
    }

    [Fact]
    public void Pipeline_ThreeCommands()
    {
        var ast = Parse("echo hi | cat | wc");
        Assert.NotNull(ast);
        var pipeline = Assert.IsType<PipelineNode>(ast.Pipelines[0]);
        Assert.Equal(3, pipeline.Commands.Count);
    }

    // ──── Lists (separators) ────

    [Fact]
    public void List_SemicolonSeparator()
    {
        var ast = Parse("echo hi; echo bye");
        Assert.NotNull(ast);
        Assert.Equal(2, ast.Pipelines.Count);
        Assert.Single(ast.Separators);
        Assert.Equal(TokenType.Semicolon, ast.Separators[0]);
    }

    [Fact]
    public void List_AndSeparator()
    {
        var ast = Parse("true && echo yes");
        Assert.NotNull(ast);
        Assert.Equal(2, ast.Pipelines.Count);
        Assert.Equal(TokenType.And, ast.Separators[0]);
    }

    [Fact]
    public void List_OrSeparator()
    {
        var ast = Parse("false || echo nope");
        Assert.NotNull(ast);
        Assert.Equal(2, ast.Pipelines.Count);
        Assert.Equal(TokenType.Or, ast.Separators[0]);
    }

    // ──── Assignments ────

    [Fact]
    public void Assignment_Standalone()
    {
        var ast = Parse("X=hello");
        Assert.NotNull(ast);
        var pipeline = Assert.IsType<PipelineNode>(ast.Pipelines[0]);
        var simple = Assert.IsType<SimpleCommandNode>(pipeline.Commands[0]);
        Assert.Single(simple.Assignments);
        Assert.Equal("X", simple.Assignments[0].Name);
        Assert.Equal("hello", simple.Assignments[0].Value);
        Assert.Empty(simple.Words);
    }

    [Fact]
    public void Assignment_PrefixToCommand()
    {
        var ast = Parse("X=1 echo $X");
        Assert.NotNull(ast);
        var pipeline = Assert.IsType<PipelineNode>(ast.Pipelines[0]);
        var simple = Assert.IsType<SimpleCommandNode>(pipeline.Commands[0]);
        Assert.Single(simple.Assignments);
        Assert.Equal("X", simple.Assignments[0].Name);
        Assert.Equal("1", simple.Assignments[0].Value);
        Assert.Equal(2, simple.Words.Count); // echo and $X
    }

    // ──── Redirections ────

    [Fact]
    public void Redirect_OutputToFile()
    {
        var ast = Parse("echo hi > out.txt");
        Assert.NotNull(ast);
        var pipeline = Assert.IsType<PipelineNode>(ast.Pipelines[0]);
        var simple = Assert.IsType<SimpleCommandNode>(pipeline.Commands[0]);
        Assert.Single(simple.Redirects);
        Assert.Equal(TokenType.GreaterThan, simple.Redirects[0].RedirectType);
    }

    [Fact]
    public void Redirect_AppendToFile()
    {
        var ast = Parse("echo hi >> out.txt");
        Assert.NotNull(ast);
        var pipeline = Assert.IsType<PipelineNode>(ast.Pipelines[0]);
        var simple = Assert.IsType<SimpleCommandNode>(pipeline.Commands[0]);
        Assert.Single(simple.Redirects);
        Assert.Equal(TokenType.DoubleGreaterThan, simple.Redirects[0].RedirectType);
    }

    [Fact]
    public void Redirect_InputFromFile()
    {
        var ast = Parse("cat < in.txt");
        Assert.NotNull(ast);
        var pipeline = Assert.IsType<PipelineNode>(ast.Pipelines[0]);
        var simple = Assert.IsType<SimpleCommandNode>(pipeline.Commands[0]);
        Assert.Single(simple.Redirects);
        Assert.Equal(TokenType.LessThan, simple.Redirects[0].RedirectType);
    }

    // ──── If Statement ────

    [Fact]
    public void If_Simple()
    {
        var ast = Parse("if true; then echo yes; fi");
        Assert.NotNull(ast);
        var pipeline = Assert.IsType<PipelineNode>(ast.Pipelines[0]);
        var ifNode = Assert.IsType<IfNode>(pipeline.Commands[0]);
        Assert.NotNull(ifNode.Condition);
        Assert.NotNull(ifNode.ThenBody);
        Assert.Null(ifNode.ElseBody);
        Assert.Empty(ifNode.ElifBranches);
    }

    [Fact]
    public void If_WithElse()
    {
        var ast = Parse("if true; then echo yes; else echo no; fi");
        Assert.NotNull(ast);
        var pipeline = Assert.IsType<PipelineNode>(ast.Pipelines[0]);
        var ifNode = Assert.IsType<IfNode>(pipeline.Commands[0]);
        Assert.NotNull(ifNode.ElseBody);
    }

    [Fact]
    public void If_WithElif()
    {
        var ast = Parse("if false; then echo a; elif true; then echo b; fi");
        Assert.NotNull(ast);
        var pipeline = Assert.IsType<PipelineNode>(ast.Pipelines[0]);
        var ifNode = Assert.IsType<IfNode>(pipeline.Commands[0]);
        Assert.Single(ifNode.ElifBranches);
    }

    // ──── For Loop ────

    [Fact]
    public void For_Simple()
    {
        var ast = Parse("for x in a b c; do echo $x; done");
        Assert.NotNull(ast);
        var pipeline = Assert.IsType<PipelineNode>(ast.Pipelines[0]);
        var forNode = Assert.IsType<ForNode>(pipeline.Commands[0]);
        Assert.Equal("x", forNode.VariableName);
        Assert.Equal(3, forNode.IterableWords.Count);
        Assert.NotNull(forNode.Body);
    }

    [Fact]
    public void For_NoInClause()
    {
        var ast = Parse("for x; do echo $x; done");
        Assert.NotNull(ast);
        var pipeline = Assert.IsType<PipelineNode>(ast.Pipelines[0]);
        var forNode = Assert.IsType<ForNode>(pipeline.Commands[0]);
        Assert.Empty(forNode.IterableWords);
    }

    // ──── While Loop ────

    [Fact]
    public void While_Simple()
    {
        var ast = Parse("while true; do echo hi; done");
        Assert.NotNull(ast);
        var pipeline = Assert.IsType<PipelineNode>(ast.Pipelines[0]);
        var whileNode = Assert.IsType<WhileNode>(pipeline.Commands[0]);
        Assert.False(whileNode.IsUntil);
        Assert.NotNull(whileNode.Condition);
        Assert.NotNull(whileNode.Body);
    }

    [Fact]
    public void Until_Simple()
    {
        var ast = Parse("until true; do echo hi; done");
        Assert.NotNull(ast);
        var pipeline = Assert.IsType<PipelineNode>(ast.Pipelines[0]);
        var whileNode = Assert.IsType<WhileNode>(pipeline.Commands[0]);
        Assert.True(whileNode.IsUntil);
    }

    // ──── Case Statement ────

    [Fact]
    public void Case_Simple()
    {
        var ast = Parse("case $x in a) echo A ;; b) echo B ;; esac");
        Assert.NotNull(ast);
        var pipeline = Assert.IsType<PipelineNode>(ast.Pipelines[0]);
        var caseNode = Assert.IsType<CaseNode>(pipeline.Commands[0]);
        Assert.Equal(2, caseNode.Items.Count);
    }

    [Fact]
    public void Case_WithWildcard()
    {
        var ast = Parse("case $x in *) echo default ;; esac");
        Assert.NotNull(ast);
        var pipeline = Assert.IsType<PipelineNode>(ast.Pipelines[0]);
        var caseNode = Assert.IsType<CaseNode>(pipeline.Commands[0]);
        Assert.Single(caseNode.Items);
    }

    // ──── Functions ────

    [Fact]
    public void Function_KeywordSyntax()
    {
        var ast = Parse("function greet { echo hello; }");
        Assert.NotNull(ast);
        var pipeline = Assert.IsType<PipelineNode>(ast.Pipelines[0]);
        var funcNode = Assert.IsType<FunctionNode>(pipeline.Commands[0]);
        Assert.Equal("greet", funcNode.Name);
        Assert.NotNull(funcNode.Body);
    }

    [Fact]
    public void Function_NameParensSyntax()
    {
        var ast = Parse("greet() { echo hello; }");
        Assert.NotNull(ast);
        var pipeline = Assert.IsType<PipelineNode>(ast.Pipelines[0]);
        var funcNode = Assert.IsType<FunctionNode>(pipeline.Commands[0]);
        Assert.Equal("greet", funcNode.Name);
        Assert.NotNull(funcNode.Body);
    }

    // ──── Word Parts / Quoting ────

    [Fact]
    public void WordParts_MergedQuoting()
    {
        var ast = Parse("echo \"hello\"world");
        Assert.NotNull(ast);
        var pipeline = Assert.IsType<PipelineNode>(ast.Pipelines[0]);
        var simple = Assert.IsType<SimpleCommandNode>(pipeline.Commands[0]);
        // "hello"world should be one word with two parts
        Assert.Equal(2, simple.Words.Count); // "echo" and "helloworld"
        var helloParts = simple.Words[1];
        Assert.Equal(2, helloParts.Count);
        Assert.Equal(WordQuoting.Double, helloParts[0].Quoting);
        Assert.Equal("hello", helloParts[0].Text);
        Assert.Equal(WordQuoting.None, helloParts[1].Quoting);
        Assert.Equal("world", helloParts[1].Text);
    }

    [Fact]
    public void Comments_Skipped()
    {
        var ast = Parse("# comment\necho hi");
        Assert.NotNull(ast);
        Assert.Single(ast.Pipelines);
    }
}
