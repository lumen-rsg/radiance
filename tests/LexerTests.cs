using Radiance.Lexer;

namespace Radiance.Tests;

public sealed class LexerTests
{
    private static List<Token> Tokenize(string input) => new Radiance.Lexer.Lexer(input).Tokenize();

    // ──── Basic Token Types ────

    [Fact]
    public void EmptyInput_ReturnsEof()
    {
        var tokens = Tokenize("");
        Assert.Single(tokens);
        Assert.Equal(TokenType.Eof, tokens[0].Type);
    }

    [Fact]
    public void WhitespaceOnly_ReturnsEof()
    {
        var tokens = Tokenize("   \t  ");
        Assert.Single(tokens);
        Assert.Equal(TokenType.Eof, tokens[0].Type);
    }

    [Fact]
    public void SimpleWord_ReturnsWordToken()
    {
        var tokens = Tokenize("echo");
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.Word, tokens[0].Type);
        Assert.Equal("echo", tokens[0].Value);
        Assert.Equal(TokenType.Eof, tokens[1].Type);
    }

    [Fact]
    public void MultipleWords_ReturnsMultipleWordTokens()
    {
        var tokens = Tokenize("echo hello world");
        Assert.Equal(4, tokens.Count); // 3 words + EOF
        Assert.Equal("echo", tokens[0].Value);
        Assert.Equal("hello", tokens[1].Value);
        Assert.Equal("world", tokens[2].Value);
    }

    [Fact]
    public void Words_SeparatedByWhitespace_HasLeadingWhitespace()
    {
        var tokens = Tokenize("echo hello");
        Assert.False(tokens[0].HasLeadingWhitespace); // first token never has leading ws
        Assert.True(tokens[1].HasLeadingWhitespace);  // second token has leading ws
    }

    // ──── Operators ────

    [Fact]
    public void Pipe_ReturnsPipeToken()
    {
        var tokens = Tokenize("|");
        Assert.Equal(TokenType.Pipe, tokens[0].Type);
    }

    [Fact]
    public void And_And_ReturnsAndToken()
    {
        var tokens = Tokenize("&&");
        Assert.Equal(TokenType.And, tokens[0].Type);
    }

    [Fact]
    public void Or_Or_ReturnsOrToken()
    {
        var tokens = Tokenize("||");
        Assert.Equal(TokenType.Or, tokens[0].Type);
    }

    [Fact]
    public void Semicolon_ReturnsSemicolonToken()
    {
        var tokens = Tokenize(";");
        Assert.Equal(TokenType.Semicolon, tokens[0].Type);
    }

    [Fact]
    public void DoubleSemicolon_ReturnsDoubleSemicolonToken()
    {
        var tokens = Tokenize(";;");
        Assert.Equal(TokenType.DoubleSemicolon, tokens[0].Type);
    }

    [Fact]
    public void Ampersand_ReturnsAmpersandToken()
    {
        var tokens = Tokenize("&");
        Assert.Equal(TokenType.Ampersand, tokens[0].Type);
    }

    [Fact]
    public void GreaterThan_ReturnsGreaterThanToken()
    {
        var tokens = Tokenize(">");
        Assert.Equal(TokenType.GreaterThan, tokens[0].Type);
    }

    [Fact]
    public void DoubleGreaterThan_ReturnsDoubleGreaterThanToken()
    {
        var tokens = Tokenize(">>");
        Assert.Equal(TokenType.DoubleGreaterThan, tokens[0].Type);
    }

    [Fact]
    public void LessThan_ReturnsLessThanToken()
    {
        var tokens = Tokenize("<");
        Assert.Equal(TokenType.LessThan, tokens[0].Type);
    }

    [Fact]
    public void LParen_ReturnsLParenToken()
    {
        var tokens = Tokenize("(");
        Assert.Equal(TokenType.LParen, tokens[0].Type);
    }

    [Fact]
    public void RParen_ReturnsRParenToken()
    {
        var tokens = Tokenize(")");
        Assert.Equal(TokenType.RParen, tokens[0].Type);
    }

    [Fact]
    public void LBrace_ReturnsWordToken()
    {
        // '{' is a reserved word, not an operator — it tokenizes as a Word
        var tokens = Tokenize("{");
        Assert.Equal(TokenType.Word, tokens[0].Type);
        Assert.Equal("{", tokens[0].Value);
    }

    [Fact]
    public void RBrace_ReturnsWordToken()
    {
        // '}' is a reserved word, not an operator — it tokenizes as a Word
        var tokens = Tokenize("}");
        Assert.Equal(TokenType.Word, tokens[0].Type);
        Assert.Equal("}", tokens[0].Value);
    }

    // ──── Comments ────

    [Fact]
    public void Comment_ReturnsCommentToken()
    {
        var tokens = Tokenize("# this is a comment");
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.Comment, tokens[0].Type);
        Assert.Equal("# this is a comment", tokens[0].Value);
    }

    [Fact]
    public void CommentAfterWord_ReturnsWordThenComment()
    {
        var tokens = Tokenize("echo # comment");
        Assert.Equal(3, tokens.Count);
        Assert.Equal(TokenType.Word, tokens[0].Type);
        Assert.Equal("echo", tokens[0].Value);
        Assert.Equal(TokenType.Comment, tokens[1].Type);
    }

    // ──── Newlines ────

    [Fact]
    public void Newline_ReturnsNewlineToken()
    {
        var tokens = Tokenize("\n");
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.Newline, tokens[0].Type);
    }

    [Fact]
    public void NewlineBetweenCommands()
    {
        var tokens = Tokenize("echo\nls");
        Assert.Equal(4, tokens.Count);
        Assert.Equal("echo", tokens[0].Value);
        Assert.Equal(TokenType.Newline, tokens[1].Type);
        Assert.Equal("ls", tokens[2].Value);
    }

    // ──── Quoting ────

    [Fact]
    public void SingleQuotedString_ReturnsSingleQuotedToken()
    {
        var tokens = Tokenize("'hello world'");
        Assert.Equal(TokenType.SingleQuotedString, tokens[0].Type);
        Assert.Equal("hello world", tokens[0].Value);
    }

    [Fact]
    public void DoubleQuotedString_ReturnsDoubleQuotedToken()
    {
        var tokens = Tokenize("\"hello world\"");
        Assert.Equal(TokenType.DoubleQuotedString, tokens[0].Type);
        Assert.Equal("hello world", tokens[0].Value);
    }

    [Fact]
    public void DoubleQuotedString_BackslashEscapes()
    {
        var tokens = Tokenize("\"hello \\\"world\\\"\"");
        Assert.Equal(TokenType.DoubleQuotedString, tokens[0].Type);
        Assert.Equal("hello \"world\"", tokens[0].Value);
    }

    [Fact]
    public void DoubleQuotedString_BackslashDollarEscapes()
    {
        var tokens = Tokenize("\"\\$HOME\"");
        Assert.Equal(TokenType.DoubleQuotedString, tokens[0].Type);
        Assert.Equal("$HOME", tokens[0].Value);
    }

    [Fact]
    public void AdjacentQuotedAndUnquoted_MergedIntoSingleToken()
    {
        var tokens = Tokenize("hello\"world\"");
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.Word, tokens[0].Type);
        Assert.Equal("helloworld", tokens[0].Value);
    }

    // ──── Assignment Words ────

    [Fact]
    public void AssignmentWord_ReturnsAssignmentTokenType()
    {
        var tokens = Tokenize("VAR=value");
        Assert.Equal(TokenType.AssignmentWord, tokens[0].Type);
        Assert.Equal("VAR=value", tokens[0].Value);
    }

    [Fact]
    public void AssignmentWithQuotedValue_ReturnsAssignmentWord()
    {
        var tokens = Tokenize("VAR=\"hello\"");
        // This should produce an assignment word token
        Assert.Equal(TokenType.AssignmentWord, tokens[0].Type);
    }

    [Fact]
    public void NoAssignment_StartsWithDigit_NotAssignment()
    {
        var tokens = Tokenize("0=bad");
        Assert.Equal(TokenType.Word, tokens[0].Type);
    }

    [Fact]
    public void NoAssignment_StartsWithEquals_NotAssignment()
    {
        var tokens = Tokenize("=value");
        Assert.Equal(TokenType.Word, tokens[0].Type);
    }

    // ──── Command Substitution ────

    [Fact]
    public void CommandSubstitution_InWord()
    {
        var tokens = Tokenize("$(echo hi)");
        Assert.Equal(TokenType.Word, tokens[0].Type);
        Assert.Equal("$(echo hi)", tokens[0].Value);
    }

    [Fact]
    public void BacktickSubstitution_InWord()
    {
        var tokens = Tokenize("`echo hi`");
        Assert.Equal(TokenType.Word, tokens[0].Type);
        Assert.Equal("`echo hi`", tokens[0].Value);
    }

    // ──── Backslash Escapes ────

    [Fact]
    public void BackslashEscape_InUnquotedWord()
    {
        var tokens = Tokenize("hello\\ world");
        Assert.Equal(TokenType.Word, tokens[0].Type);
        Assert.Equal("hello world", tokens[0].Value);
    }

    // ──── Complex Inputs ────

    [Fact]
    public void Pipeline_TokenizedCorrectly()
    {
        var tokens = Tokenize("echo hello | cat");
        Assert.Equal(5, tokens.Count); // echo hello | cat EOF
        Assert.Equal("echo", tokens[0].Value);
        Assert.Equal("hello", tokens[1].Value);
        Assert.Equal(TokenType.Pipe, tokens[2].Type);
        Assert.Equal("cat", tokens[3].Value);
    }

    [Fact]
    public void ComplexCommand_TokenizedCorrectly()
    {
        var tokens = Tokenize("X=1 echo $X > out.txt");
        Assert.Equal(TokenType.AssignmentWord, tokens[0].Type);
        Assert.Equal("X=1", tokens[0].Value);
        Assert.Equal("echo", tokens[1].Value);
        Assert.Equal("$X", tokens[2].Value);
        Assert.Equal(TokenType.GreaterThan, tokens[3].Type);
        Assert.Equal("out.txt", tokens[4].Value);
    }
}
