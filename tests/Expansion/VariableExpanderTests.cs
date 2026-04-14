using Radiance.Expansion;
using Radiance.Interpreter;

namespace Radiance.Tests.Expansion;

public sealed class VariableExpanderTests
{
    private ShellContext CreateContext()
    {
        var ctx = new ShellContext();
        ctx.SetVariable("HOME", "/home/testuser");
        ctx.SetVariable("USER", "testuser");
        ctx.SetVariable("EMPTY", "");
        ctx.LastExitCode = 42;
        return ctx;
    }

    // ──── Basic Variable Expansion ────

    [Fact]
    public void Expand_SimpleVariable()
    {
        var ctx = CreateContext();
        var result = VariableExpander.Expand("$HOME", ctx);
        Assert.Equal("/home/testuser", result);
    }

    [Fact]
    public void Expand_BracedVariable()
    {
        var ctx = CreateContext();
        var result = VariableExpander.Expand("${HOME}", ctx);
        Assert.Equal("/home/testuser", result);
    }

    [Fact]
    public void Expand_UndefinedVariable_ReturnsEmpty()
    {
        var ctx = CreateContext();
        var result = VariableExpander.Expand("$NONEXISTENT", ctx);
        Assert.Equal("", result);
    }

    [Fact]
    public void Expand_VariableInString()
    {
        var ctx = CreateContext();
        var result = VariableExpander.Expand("Hello, $USER!", ctx);
        Assert.Equal("Hello, testuser!", result);
    }

    [Fact]
    public void Expand_MultipleVariables()
    {
        var ctx = CreateContext();
        var result = VariableExpander.Expand("$HOME/$USER", ctx);
        Assert.Equal("/home/testuser/testuser", result);
    }

    // ──── Special Variables ────

    [Fact]
    public void Expand_ExitCode()
    {
        var ctx = CreateContext();
        var result = VariableExpander.Expand("$?", ctx);
        Assert.Equal("42", result);
    }

    [Fact]
    public void Expand_ShellPid()
    {
        var ctx = CreateContext();
        var result = VariableExpander.Expand("$$", ctx);
        Assert.Equal(Environment.ProcessId.ToString(), result);
    }

    [Fact]
    public void Expand_PositionalCount()
    {
        var ctx = CreateContext();
        ctx.SetPositionalParams(["a", "b", "c"]);
        var result = VariableExpander.Expand("$#", ctx);
        Assert.Equal("3", result);
    }

    [Fact]
    public void Expand_AllPositional_At()
    {
        var ctx = CreateContext();
        ctx.SetPositionalParams(["x", "y", "z"]);
        var result = VariableExpander.Expand("$@", ctx);
        Assert.Equal("x y z", result);
    }

    [Fact]
    public void Expand_AllPositional_Star()
    {
        var ctx = CreateContext();
        ctx.SetPositionalParams(["x", "y", "z"]);
        var result = VariableExpander.Expand("$*", ctx);
        Assert.Equal("x y z", result);
    }

    [Fact]
    public void Expand_ShellName()
    {
        var ctx = CreateContext();
        ctx.ShellName = "test_shell";
        var result = VariableExpander.Expand("$0", ctx);
        Assert.Equal("test_shell", result);
    }

    [Fact]
    public void Expand_PositionalParam()
    {
        var ctx = CreateContext();
        ctx.SetPositionalParams(["one", "two", "three"]);
        Assert.Equal("one", VariableExpander.Expand("$1", ctx));
        Assert.Equal("two", VariableExpander.Expand("$2", ctx));
        Assert.Equal("three", VariableExpander.Expand("$3", ctx));
    }

    // ──── Parameter Expansion ────

    [Fact]
    public void Expand_DefaultIfEmpty()
    {
        var ctx = CreateContext();
        var result = VariableExpander.Expand("${UNSET_VAR:-fallback}", ctx);
        Assert.Equal("fallback", result);
    }

    [Fact]
    public void Expand_DefaultIfEmpty_NotUsedWhenSet()
    {
        var ctx = CreateContext();
        var result = VariableExpander.Expand("${HOME:-fallback}", ctx);
        Assert.Equal("/home/testuser", result);
    }

    [Fact]
    public void Expand_AssignDefault()
    {
        var ctx = CreateContext();
        var result = VariableExpander.Expand("${NEW_VAR:=default_val}", ctx);
        Assert.Equal("default_val", result);
        Assert.Equal("default_val", ctx.GetVariable("NEW_VAR"));
    }

    [Fact]
    public void Expand_AlternativeIfSet()
    {
        var ctx = CreateContext();
        var result = VariableExpander.Expand("${HOME:+was_set}", ctx);
        Assert.Equal("was_set", result);
    }

    [Fact]
    public void Expand_AlternativeIfSet_Unset()
    {
        var ctx = CreateContext();
        var result = VariableExpander.Expand("${UNSET_VAR:+was_set}", ctx);
        Assert.Equal("", result);
    }

    [Fact]
    public void Expand_StringLength()
    {
        var ctx = CreateContext();
        var result = VariableExpander.Expand("${#HOME}", ctx);
        Assert.Equal("/home/testuser".Length.ToString(), result);
    }

    // ──── Edge Cases ────

    [Fact]
    public void Expand_NoDollar_ReturnsAsIs()
    {
        var ctx = CreateContext();
        var result = VariableExpander.Expand("no variables here", ctx);
        Assert.Equal("no variables here", result);
    }

    [Fact]
    public void Expand_EmptyString()
    {
        var ctx = CreateContext();
        var result = VariableExpander.Expand("", ctx);
        Assert.Equal("", result);
    }

    [Fact]
    public void Expand_LoneDollar()
    {
        var ctx = CreateContext();
        var result = VariableExpander.Expand("$", ctx);
        Assert.Equal("$", result);
    }

    [Fact]
    public void Expand_DollarFollowedByNonIdentifier()
    {
        var ctx = CreateContext();
        var result = VariableExpander.Expand("$!", ctx);
        // $! is the last background PID
        Assert.Equal("0", result); // default is 0
    }
}
