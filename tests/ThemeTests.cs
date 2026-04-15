using Radiance.Themes;
using Radiance.Themes.Builtins;
using Xunit;

namespace Radiance.Tests;

public class ThemeTests
{
    private static PromptContext MakeContext(Action<PromptContext>? configure = null)
    {
        var ctx = new PromptContext
        {
            User = "testuser",
            Host = "testhost",
            HomeDir = "/home/testuser",
            Cwd = "/home/testuser/projects/radiance",
            LastExitCode = 0,
            IsRoot = false,
            GitBranch = null,
            GitDirty = false,
            JobCount = 0,
            Now = new DateTime(2026, 1, 15, 14, 30, 0),
            ShellName = "radiance"
        };
        configure?.Invoke(ctx);
        return ctx;
    }

    // ─── PromptContext Tests ─────────────────────────────────────────

    [Fact]
    public void PromptContext_TildeCwd_ReplacesHomeDir()
    {
        var ctx = MakeContext();
        Assert.Equal("~/projects/radiance", ctx.TildeCwd);
    }

    [Fact]
    public void PromptContext_TildeCwd_HomeOnly_ReturnsTilde()
    {
        var ctx = MakeContext(c => c.Cwd = "/home/testuser");
        Assert.Equal("~", ctx.TildeCwd);
    }

    [Fact]
    public void PromptContext_TildeCwd_OutsideHome_ReturnsFull()
    {
        var ctx = MakeContext(c => c.Cwd = "/usr/local/bin");
        Assert.Equal("/usr/local/bin", ctx.TildeCwd);
    }

    [Fact]
    public void PromptContext_ShortCwd_ReturnsLastComponent()
    {
        var ctx = MakeContext();
        Assert.Equal("radiance", ctx.ShortCwd);
    }

    [Fact]
    public void PromptContext_Time_FormatsCorrectly()
    {
        var ctx = MakeContext();
        Assert.Equal("14:30:00", ctx.Time);
    }

    [Fact]
    public void PromptContext_Date_FormatsCorrectly()
    {
        var ctx = MakeContext();
        Assert.Matches(@"\w{3} \w{3} \d{2}", ctx.Date);
    }

    // ─── ThemeBase Helper Tests ──────────────────────────────────────

    [Fact]
    public void ThemeBase_Color_WrapsInAnsi()
    {
        var result = TestTheme.DoColor("hello", AnsiColor.Red);
        Assert.Contains("\x1b[31m", result);
        Assert.Contains("hello", result);
        Assert.Contains("\x1b[0m", result);
    }

    [Fact]
    public void ThemeBase_Color_WithBold()
    {
        var result = TestTheme.DoColor("hello", AnsiColor.Green, bold: true);
        Assert.Contains("\x1b[1m", result);
        Assert.Contains("\x1b[32m", result);
        Assert.Contains("hello", result);
    }

    [Fact]
    public void ThemeBase_TextWidth_IgnoresAnsiEscapes()
    {
        var text = "\x1b[31mhello\x1b[0m";
        Assert.Equal(5, ThemeBase.TextWidth(text));
    }

    [Fact]
    public void ThemeBase_FormatGit_NoBranch_ReturnsEmpty()
    {
        var ctx = MakeContext();
        Assert.Equal("", TestTheme.DoFormatGit(ctx));
    }

    [Fact]
    public void ThemeBase_FormatGit_WithBranch()
    {
        var ctx = MakeContext(c => { c.GitBranch = "main"; });
        Assert.Equal("main", TestTheme.DoFormatGit(ctx));
    }

    [Fact]
    public void ThemeBase_FormatGit_Dirty_AppendsStar()
    {
        var ctx = MakeContext(c => { c.GitBranch = "main"; c.GitDirty = true; });
        Assert.Equal("main*", TestTheme.DoFormatGit(ctx));
    }

    [Fact]
    public void ThemeBase_FormatPromptChar_Success()
    {
        var ctx = MakeContext();
        var result = TestTheme.DoFormatPromptChar(ctx);
        Assert.Contains("$", result);
        Assert.Contains("\x1b[32m", result); // Green
    }

    [Fact]
    public void ThemeBase_FormatPromptChar_Error()
    {
        var ctx = MakeContext(c => c.LastExitCode = 1);
        var result = TestTheme.DoFormatPromptChar(ctx);
        Assert.Contains("$", result);
        Assert.Contains("\x1b[31m", result); // Red
    }

    // ─── Built-in Theme Tests ────────────────────────────────────────

    [Fact]
    public void DefaultTheme_RenderPrompt_ContainsUserAndHost()
    {
        var theme = new DefaultTheme();
        var result = theme.RenderPrompt(MakeContext());
        Assert.Contains("testuser", result);
        Assert.Contains("testhost", result);
    }

    [Fact]
    public void DefaultTheme_RenderPrompt_ContainsCwd()
    {
        var theme = new DefaultTheme();
        var result = theme.RenderPrompt(MakeContext());
        Assert.Contains("~/projects/radiance", result);
    }

    [Fact]
    public void DefaultTheme_RenderPrompt_WithGit()
    {
        var theme = new DefaultTheme();
        var ctx = MakeContext(c => { c.GitBranch = "main"; c.GitDirty = true; });
        var result = theme.RenderPrompt(ctx);
        Assert.Contains("main*", result);
    }

    [Fact]
    public void DefaultTheme_RenderRightPrompt_IsEmpty()
    {
        var theme = new DefaultTheme();
        Assert.Equal("", theme.RenderRightPrompt(MakeContext()));
    }

    [Fact]
    public void MinimalTheme_RenderPrompt_ContainsShortCwd()
    {
        var theme = new MinimalTheme();
        var result = theme.RenderPrompt(MakeContext());
        Assert.Contains("radiance", result);
        Assert.DoesNotContain("~/projects", result);
    }

    [Fact]
    public void MinimalTheme_RenderPrompt_ContainsArrow()
    {
        var theme = new MinimalTheme();
        var result = theme.RenderPrompt(MakeContext());
        Assert.Contains("❯", result);
    }

    [Fact]
    public void PowerlineTheme_RenderPrompt_ContainsUserAndCwd()
    {
        var theme = new PowerlineTheme();
        var result = theme.RenderPrompt(MakeContext());
        Assert.Contains("testuser", result);
        Assert.Contains("~/projects/radiance", result);
    }

    [Fact]
    public void PowerlineTheme_RenderPrompt_TwoLines()
    {
        var theme = new PowerlineTheme();
        var result = theme.RenderPrompt(MakeContext());
        Assert.Contains("\n", result);
    }

    [Fact]
    public void RainbowTheme_RenderPrompt_ContainsUserAndHost()
    {
        var theme = new RainbowTheme();
        var result = theme.RenderPrompt(MakeContext());
        Assert.Contains("testuser", result);
        Assert.Contains("testhost", result);
    }

    [Fact]
    public void RainbowTheme_RenderPrompt_WithExitCode()
    {
        var theme = new RainbowTheme();
        var ctx = MakeContext(c => c.LastExitCode = 42);
        var result = theme.RenderPrompt(ctx);
        Assert.Contains("42", result);
    }

    [Fact]
    public void DarkTheme_RenderPrompt_TwoLines()
    {
        var theme = new DarkTheme();
        var result = theme.RenderPrompt(MakeContext());
        Assert.Contains("\n", result);
    }

    [Fact]
    public void DarkTheme_RenderRightPrompt_ContainsUserHost()
    {
        var theme = new DarkTheme();
        var result = theme.RenderRightPrompt(MakeContext());
        Assert.Contains("testuser", result);
        Assert.Contains("testhost", result);
    }

    [Fact]
    public void LightTheme_RenderPrompt_ContainsCwd()
    {
        var theme = new LightTheme();
        var result = theme.RenderPrompt(MakeContext());
        Assert.Contains("~/projects/radiance", result);
    }

    // ─── ThemeManager Tests ──────────────────────────────────────────

    [Fact]
    public void ThemeManager_DefaultTheme_IsDefault()
    {
        var manager = new ThemeManager();
        manager.Initialize();
        Assert.Equal("default", manager.ActiveTheme.Name);
    }

    [Fact]
    public void ThemeManager_HasBuiltInThemes()
    {
        var manager = new ThemeManager();
        manager.Initialize();
        Assert.True(manager.HasTheme("default"));
        Assert.True(manager.HasTheme("minimal"));
        Assert.True(manager.HasTheme("powerline"));
        Assert.True(manager.HasTheme("rainbow"));
        Assert.True(manager.HasTheme("dark"));
        Assert.True(manager.HasTheme("light"));
    }

    [Fact]
    public void ThemeManager_SetTheme_ChangesActiveTheme()
    {
        var manager = new ThemeManager();
        manager.Initialize();
        Assert.True(manager.SetTheme("minimal"));
        Assert.Equal("minimal", manager.ActiveTheme.Name);
    }

    [Fact]
    public void ThemeManager_SetTheme_Unknown_ReturnsFalse()
    {
        var manager = new ThemeManager();
        manager.Initialize();
        Assert.False(manager.SetTheme("nonexistent"));
        Assert.Equal("default", manager.ActiveTheme.Name);
    }

    [Fact]
    public void ThemeManager_SetTheme_CaseInsensitive()
    {
        var manager = new ThemeManager();
        manager.Initialize();
        Assert.True(manager.SetTheme("MINIMAL"));
        Assert.Equal("minimal", manager.ActiveTheme.Name);
    }

    [Fact]
    public void ThemeManager_GetTheme_ReturnsTheme()
    {
        var manager = new ThemeManager();
        manager.Initialize();
        var theme = manager.GetTheme("powerline");
        Assert.NotNull(theme);
        Assert.Equal("powerline", theme.Name);
    }

    [Fact]
    public void ThemeManager_GetTheme_Unknown_ReturnsNull()
    {
        var manager = new ThemeManager();
        manager.Initialize();
        Assert.Null(manager.GetTheme("nonexistent"));
    }

    [Fact]
    public void ThemeManager_Count_IncludesBuiltIns()
    {
        var manager = new ThemeManager();
        manager.Initialize();
        Assert.Equal(6, manager.Count);
    }

    [Fact]
    public void ThemeManager_RegisterCustomTheme()
    {
        var manager = new ThemeManager();
        manager.Initialize();
        var custom = new TestTheme();
        manager.RegisterTheme(custom);
        Assert.True(manager.HasTheme("test"));
        Assert.Equal(7, manager.Count);
    }

    [Fact]
    public void ThemeManager_RenderPrompt_UsesActiveTheme()
    {
        var manager = new ThemeManager();
        manager.Initialize();
        manager.SetTheme("minimal");
        var result = manager.RenderPrompt(MakeContext());
        Assert.Contains("radiance", result);
    }

    // ─── JsonTheme Tests ─────────────────────────────────────────────

    [Fact]
    public void JsonTheme_LoadFromValidJson()
    {
        var json = """
        {
            "name": "test-json",
            "description": "A test JSON theme",
            "author": "Tester",
            "left_prompt": [
                { "type": "user", "fg": "green", "bold": true, "suffix": "@" },
                { "type": "host", "fg": "cyan", "suffix": " " },
                { "type": "cwd", "fg": "blue", "suffix": " " },
                { "type": "prompt_char", "fg": "white", "text": "$ " }
            ]
        }
        """;
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var theme = JsonTheme.LoadFromFile(tempFile);
            Assert.NotNull(theme);
            Assert.Equal("test-json", theme.Name);
            Assert.Equal("A test JSON theme", theme.Description);
            Assert.Equal("Tester", theme.Author);

            var result = theme.RenderPrompt(MakeContext());
            Assert.Contains("testuser", result);
            Assert.Contains("testhost", result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void JsonTheme_LoadFromInvalidJson_ReturnsNull()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "not valid json!!!");
            var theme = JsonTheme.LoadFromFile(tempFile);
            Assert.Null(theme);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void JsonTheme_GitSegment_WithBranch()
    {
        var json = """
        {
            "name": "git-test",
            "left_prompt": [
                { "type": "cwd", "fg": "blue", "suffix": " " },
                { "type": "git", "fg": "magenta", "dirty_fg": "red", "prefix": "(", "suffix": ") " },
                { "type": "prompt_char", "fg": "green", "text": "$ " }
            ]
        }
        """;
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var theme = JsonTheme.LoadFromFile(tempFile);
            Assert.NotNull(theme);

            var ctx = MakeContext(c => { c.GitBranch = "develop"; c.GitDirty = true; });
            var result = theme.RenderPrompt(ctx);
            Assert.Contains("develop*", result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void JsonTheme_PromptChar_ErrorColor()
    {
        var json = """
        {
            "name": "error-test",
            "left_prompt": [
                { "type": "prompt_char", "fg": "green", "error_fg": "red", "text": "❯ " }
            ]
        }
        """;
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var theme = JsonTheme.LoadFromFile(tempFile);
            Assert.NotNull(theme);

            var ctx = MakeContext(c => c.LastExitCode = 1);
            var result = theme.RenderPrompt(ctx);
            Assert.Contains("❯", result);
            Assert.Contains("\x1b[31m", result); // Red for error
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void JsonTheme_RightPrompt()
    {
        var json = """
        {
            "name": "rprompt-test",
            "right_prompt": [
                { "type": "time", "fg": "dark_gray" }
            ]
        }
        """;
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var theme = JsonTheme.LoadFromFile(tempFile);
            Assert.NotNull(theme);

            var result = theme.RenderRightPrompt(MakeContext());
            Assert.Contains("14:30:00", result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ─── Helper Test Theme ───────────────────────────────────────────

    /// <summary>
    /// A test theme that exposes ThemeBase protected methods for testing.
    /// </summary>
    private class TestTheme : ThemeBase
    {
        public override string Name => "test";
        public override string Description => "Test theme";
        public override string Author => "Tests";

        public override string RenderPrompt(PromptContext ctx) => "test> ";
        public override string RenderRightPrompt(PromptContext ctx) => "";

        public static string DoColor(string text, AnsiColor fg, bool bold = false) => Color(text, fg, bold);
        public static string DoFormatGit(PromptContext ctx, string? prefix = null, string? suffix = null)
            => FormatGit(ctx, prefix, suffix);
        public static string DoFormatPromptChar(PromptContext ctx, string ch = "$",
            AnsiColor success = AnsiColor.Green, AnsiColor error = AnsiColor.Red)
            => FormatPromptChar(ctx, ch, success, error);
    }
}