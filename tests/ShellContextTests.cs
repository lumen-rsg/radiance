using Radiance.Interpreter;

namespace Radiance.Tests;

public sealed class ShellContextTests
{
    private ShellContext CreateContext() => new();

    // ──── Variable CRUD ────

    [Fact]
    public void SetAndGet_Variable()
    {
        var ctx = CreateContext();
        ctx.SetVariable("MY_VAR", "hello");
        Assert.Equal("hello", ctx.GetVariable("MY_VAR"));
    }

    [Fact]
    public void GetVariable_Undefined_ReturnsEmpty()
    {
        var ctx = CreateContext();
        Assert.Equal("", ctx.GetVariable("UNDEFINED_VAR"));
    }

    [Fact]
    public void SetVariable_Overwrite()
    {
        var ctx = CreateContext();
        ctx.SetVariable("X", "1");
        ctx.SetVariable("X", "2");
        Assert.Equal("2", ctx.GetVariable("X"));
    }

    [Fact]
    public void UnsetVariable_RemovesVariable()
    {
        var ctx = CreateContext();
        ctx.SetVariable("X", "1");
        ctx.UnsetVariable("X");
        Assert.Equal("", ctx.GetVariable("X"));
    }

    // ──── Scoping ────

    [Fact]
    public void PushScope_IsolatesInnerScope()
    {
        var ctx = CreateContext();
        ctx.SetVariable("X", "global");
        ctx.PushScope();
        ctx.SetLocalVariable("X", "local");
        Assert.Equal("local", ctx.GetVariable("X"));
        ctx.PopScope();
        Assert.Equal("global", ctx.GetVariable("X"));
    }

    [Fact]
    public void SetLocalVariable_SetsInCurrentScope()
    {
        var ctx = CreateContext();
        ctx.SetVariable("X", "global");
        ctx.PushScope();
        ctx.SetLocalVariable("Y", "local_only");
        Assert.Equal("local_only", ctx.GetVariable("Y"));
        ctx.PopScope();
        Assert.Equal("", ctx.GetVariable("Y"));
    }

    [Fact]
    public void ScopeDepth_StartsAtOne()
    {
        var ctx = CreateContext();
        Assert.Equal(1, ctx.ScopeDepth);
    }

    [Fact]
    public void ScopeDepth_IncreasesWithPush()
    {
        var ctx = CreateContext();
        ctx.PushScope();
        Assert.Equal(2, ctx.ScopeDepth);
        ctx.PushScope();
        Assert.Equal(3, ctx.ScopeDepth);
        ctx.PopScope();
        Assert.Equal(2, ctx.ScopeDepth);
    }

    [Fact]
    public void PopScope_AtGlobal_DoesNotPop()
    {
        var ctx = CreateContext();
        ctx.PopScope(); // should not throw
        Assert.Equal(1, ctx.ScopeDepth);
    }

    // ──── Export ────

    [Fact]
    public void ExportVariable_SetsEnvironment()
    {
        var ctx = CreateContext();
        ctx.ExportVariable("TEST_RAD_VAR", "exported_value");
        Assert.Equal("exported_value", Environment.GetEnvironmentVariable("TEST_RAD_VAR"));
        // Cleanup
        Environment.SetEnvironmentVariable("TEST_RAD_VAR", null);
    }

    [Fact]
    public void ExportVariable_WithValue()
    {
        var ctx = CreateContext();
        ctx.ExportVariable("TEST_RAD_VAR2", "direct_export");
        Assert.Equal("direct_export", ctx.GetVariable("TEST_RAD_VAR2"));
        Assert.Equal("direct_export", Environment.GetEnvironmentVariable("TEST_RAD_VAR2"));
        // Cleanup
        Environment.SetEnvironmentVariable("TEST_RAD_VAR2", null);
    }

    [Fact]
    public void IsExported_ReturnsTrue()
    {
        var ctx = CreateContext();
        ctx.ExportVariable("TEST_RAD_EXPORT", "val");
        Assert.True(ctx.IsExported("TEST_RAD_EXPORT"));
        Environment.SetEnvironmentVariable("TEST_RAD_EXPORT", null);
    }

    [Fact]
    public void UnsetVariable_RemovesExport()
    {
        var ctx = CreateContext();
        ctx.ExportVariable("TEST_RAD_UNSET", "val");
        ctx.UnsetVariable("TEST_RAD_UNSET");
        Assert.False(ctx.IsExported("TEST_RAD_UNSET"));
        Assert.Null(Environment.GetEnvironmentVariable("TEST_RAD_UNSET"));
    }

    // ──── Functions ────

    [Fact]
    public void SetFunction_RegistersFunction()
    {
        var ctx = CreateContext();
        var body = new Radiance.Parser.Ast.ListNode();
        ctx.SetFunction("myfunc", body);
        Assert.True(ctx.HasFunction("myfunc"));
    }

    [Fact]
    public void GetFunction_ReturnsBody()
    {
        var ctx = CreateContext();
        var body = new Radiance.Parser.Ast.ListNode();
        ctx.SetFunction("myfunc", body);
        var result = ctx.GetFunction("myfunc");
        Assert.NotNull(result);
        Assert.Equal("myfunc", result.Name);
    }

    [Fact]
    public void UnsetFunction_RemovesFunction()
    {
        var ctx = CreateContext();
        ctx.SetFunction("myfunc", new Radiance.Parser.Ast.ListNode());
        ctx.UnsetFunction("myfunc");
        Assert.False(ctx.HasFunction("myfunc"));
    }

    // ──── Aliases ────

    [Fact]
    public void SetAlias_RegistersAlias()
    {
        var ctx = CreateContext();
        ctx.SetAlias("ll", "ls -la");
        Assert.Equal("ls -la", ctx.GetAlias("ll"));
    }

    [Fact]
    public void UnsetAlias_RemovesAlias()
    {
        var ctx = CreateContext();
        ctx.SetAlias("ll", "ls -la");
        ctx.UnsetAlias("ll");
        Assert.Null(ctx.GetAlias("ll"));
    }

    [Fact]
    public void UnsetAllAliases_ClearsAll()
    {
        var ctx = CreateContext();
        ctx.SetAlias("a", "cmd_a");
        ctx.SetAlias("b", "cmd_b");
        ctx.UnsetAllAliases();
        Assert.Empty(ctx.Aliases);
    }

    // ──── Positional Parameters ────

    [Fact]
    public void SetPositionalParams_Basic()
    {
        var ctx = CreateContext();
        ctx.SetPositionalParams(["arg1", "arg2", "arg3"]);
        Assert.Equal("arg1", ctx.GetPositionalParam(1));
        Assert.Equal("arg2", ctx.GetPositionalParam(2));
        Assert.Equal("arg3", ctx.GetPositionalParam(3));
        Assert.Equal(3, ctx.PositionalParamCount);
    }

    [Fact]
    public void GetPositionalParam_OutOfRange_ReturnsEmpty()
    {
        var ctx = CreateContext();
        Assert.Equal("", ctx.GetPositionalParam(0));
        Assert.Equal("", ctx.GetPositionalParam(1));
        Assert.Equal("", ctx.GetPositionalParam(99));
    }

    [Fact]
    public void PushPopPositionalParams_Restores()
    {
        var ctx = CreateContext();
        ctx.SetPositionalParams(["old1", "old2"]);
        ctx.PushPositionalParams(["new1", "new2", "new3"]);
        Assert.Equal(3, ctx.PositionalParamCount);
        Assert.Equal("new1", ctx.GetPositionalParam(1));
        ctx.PopPositionalParams();
        Assert.Equal(2, ctx.PositionalParamCount);
        Assert.Equal("old1", ctx.GetPositionalParam(1));
    }

    [Fact]
    public void ShellName_DefaultsToRadiance()
    {
        var ctx = CreateContext();
        Assert.Equal("radiance", ctx.ShellName);
    }

    // ──── Environment Fallback ────

    [Fact]
    public void GetVariable_FallsBackToEnvironment()
    {
        var ctx = CreateContext();
        Environment.SetEnvironmentVariable("TEST_RAD_ENV", "env_value");
        Assert.Equal("env_value", ctx.GetVariable("TEST_RAD_ENV"));
        Environment.SetEnvironmentVariable("TEST_RAD_ENV", null);
    }

    // ──── Shell Variable Names ────

    [Fact]
    public void ShellVariableNames_ReturnsAllScopes()
    {
        var ctx = CreateContext();
        ctx.SetVariable("A", "1");
        ctx.PushScope();
        ctx.SetVariable("B", "2");
        var names = ctx.ShellVariableNames.ToList();
        Assert.Contains("A", names);
        Assert.Contains("B", names);
    }
}
