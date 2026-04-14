using Radiance.Expansion;
using Radiance.Interpreter;

namespace Radiance.Tests.Expansion;

public sealed class TildeExpanderTests
{
    private readonly ShellContext _ctx = new();

    [Fact]
    public void Expand_BareTilde()
    {
        var result = TildeExpander.Expand("~", false, _ctx);
        Assert.Equal(Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), result);
    }

    [Fact]
    public void Expand_TildeSlash()
    {
        var home = Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var result = TildeExpander.Expand("~/Documents", false, _ctx);
        Assert.Equal($"{home}/Documents", result);
    }

    [Fact]
    public void Expand_NoTilde_ReturnsAsIs()
    {
        var result = TildeExpander.Expand("no/tilde/here", false, _ctx);
        Assert.Equal("no/tilde/here", result);
    }

    [Fact]
    public void Expand_TildeNotAtStart_ReturnsAsIs()
    {
        var result = TildeExpander.Expand("path/~", false, _ctx);
        Assert.Equal("path/~", result);
    }

    [Fact]
    public void Expand_EmptyString()
    {
        var result = TildeExpander.Expand("", false, _ctx);
        Assert.Equal("", result);
    }

    [Fact]
    public void Expand_TildeWithUsername()
    {
        var result = TildeExpander.Expand("~root", false, _ctx);
        Assert.NotNull(result);
    }

    [Fact]
    public void Expand_QuotedTilde_NoExpansion()
    {
        var result = TildeExpander.Expand("~", true, _ctx);
        Assert.Equal("~", result);
    }
}
