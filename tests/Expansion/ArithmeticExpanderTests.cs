using Radiance.Expansion;
using Radiance.Interpreter;

namespace Radiance.Tests.Expansion;

public sealed class ArithmeticExpanderTests
{
    private ShellContext CreateContext()
    {
        var ctx = new ShellContext();
        ctx.SetVariable("X", "10");
        ctx.SetVariable("Y", "3");
        return ctx;
    }

    [Fact]
    public void Expand_Addition()
    {
        var ctx = CreateContext();
        var expander = new ArithmeticExpander(ctx);
        Assert.Equal("5", expander.Expand("$((2+3))"));
    }

    [Fact]
    public void Expand_Subtraction()
    {
        var ctx = CreateContext();
        var expander = new ArithmeticExpander(ctx);
        Assert.Equal("7", expander.Expand("$((10-3))"));
    }

    [Fact]
    public void Expand_Multiplication()
    {
        var ctx = CreateContext();
        var expander = new ArithmeticExpander(ctx);
        Assert.Equal("12", expander.Expand("$((3*4))"));
    }

    [Fact]
    public void Expand_Division()
    {
        var ctx = CreateContext();
        var expander = new ArithmeticExpander(ctx);
        Assert.Equal("3", expander.Expand("$((9/3))"));
    }

    [Fact]
    public void Expand_Modulo()
    {
        var ctx = CreateContext();
        var expander = new ArithmeticExpander(ctx);
        Assert.Equal("1", expander.Expand("$((10%3))"));
    }

    [Fact]
    public void Expand_Comparison_Equals()
    {
        var ctx = CreateContext();
        var expander = new ArithmeticExpander(ctx);
        Assert.Equal("1", expander.Expand("$((5==5))"));
        Assert.Equal("0", expander.Expand("$((5==3))"));
    }

    [Fact]
    public void Expand_Comparison_NotEquals()
    {
        var ctx = CreateContext();
        var expander = new ArithmeticExpander(ctx);
        Assert.Equal("0", expander.Expand("$((5!=5))"));
        Assert.Equal("1", expander.Expand("$((5!=3))"));
    }

    [Fact]
    public void Expand_Comparison_LessThan()
    {
        var ctx = CreateContext();
        var expander = new ArithmeticExpander(ctx);
        Assert.Equal("1", expander.Expand("$((3<5))"));
        Assert.Equal("0", expander.Expand("$((5<3))"));
    }

    [Fact]
    public void Expand_LogicalAnd()
    {
        var ctx = CreateContext();
        var expander = new ArithmeticExpander(ctx);
        Assert.Equal("1", expander.Expand("$((1&&1))"));
        Assert.Equal("0", expander.Expand("$((1&&0))"));
    }

    [Fact]
    public void Expand_LogicalOr()
    {
        var ctx = CreateContext();
        var expander = new ArithmeticExpander(ctx);
        Assert.Equal("1", expander.Expand("$((0||1))"));
        Assert.Equal("0", expander.Expand("$((0||0))"));
    }

    [Fact]
    public void Expand_UnaryNegation()
    {
        var ctx = CreateContext();
        var expander = new ArithmeticExpander(ctx);
        Assert.Equal("-5", expander.Expand("$((-5))"));
    }

    [Fact]
    public void Expand_UnaryNot()
    {
        var ctx = CreateContext();
        var expander = new ArithmeticExpander(ctx);
        Assert.Equal("0", expander.Expand("$((!1))"));
        Assert.Equal("1", expander.Expand("$((!0))"));
    }

    [Fact]
    public void Expand_Parentheses()
    {
        var ctx = CreateContext();
        var expander = new ArithmeticExpander(ctx);
        Assert.Equal("14", expander.Expand("$((2*(3+4)))"));
    }

    [Fact]
    public void Expand_WithVariable()
    {
        var ctx = CreateContext();
        var expander = new ArithmeticExpander(ctx);
        Assert.Equal("13", expander.Expand("$(($X+3))"));
    }

    [Fact]
    public void Expand_BareVariableReference()
    {
        var ctx = CreateContext();
        var expander = new ArithmeticExpander(ctx);
        Assert.Equal("30", expander.Expand("$((X*Y))"));
    }

    [Fact]
    public void Expand_NoArithmetic_ReturnsAsIs()
    {
        var ctx = CreateContext();
        var expander = new ArithmeticExpander(ctx);
        Assert.Equal("hello", expander.Expand("hello"));
    }

    [Fact]
    public void Expand_EmptyString()
    {
        var ctx = CreateContext();
        var expander = new ArithmeticExpander(ctx);
        Assert.Equal("", expander.Expand(""));
    }

    [Fact]
    public void Expand_InString()
    {
        var ctx = CreateContext();
        var expander = new ArithmeticExpander(ctx);
        Assert.Equal("result is 5", expander.Expand("result is $((2+3))"));
    }
}
