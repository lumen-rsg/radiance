using System.Text;

namespace Radiance.Themes.Builtins;

/// <summary>
/// A theme optimized for dark terminal backgrounds.
/// </summary>
public sealed class DarkTheme : ThemeBase
{
    public override string Name => "dark";
    public override string Description => "Optimized for dark terminal backgrounds";
    public override string Author => "Radiance";

    public override string RenderPrompt(PromptContext ctx)
    {
        var sb = new StringBuilder();

        // Directory in cyan (high contrast on dark)
        sb.Append(Color(ctx.TildeCwd, AnsiColor.Cyan, bold: true));

        // Git info in yellow
        if (!string.IsNullOrEmpty(ctx.GitBranch))
        {
            sb.Append(' ');
            var gitInfo = ctx.GitBranch;
            if (ctx.GitDirty) gitInfo += "\u26a1";
            sb.Append(Color($"({gitInfo})", AnsiColor.Yellow));
        }

        // Exit code
        if (ctx.LastExitCode != 0)
        {
            sb.Append(' ');
            sb.Append(Color(ctx.LastExitCode.ToString(), AnsiColor.BrightRed, bold: true));
        }

        // Two-line prompt
        sb.Append('\n');
        sb.Append(Color("\u276f", ctx.LastExitCode == 0 ? AnsiColor.BrightGreen : AnsiColor.BrightRed, bold: true));
        sb.Append(' ');

        return sb.ToString();
    }

    public override string RenderRightPrompt(PromptContext ctx)
    {
        var sb = new StringBuilder();
        sb.Append(Color($"{ctx.User}@{ctx.Host}", AnsiColor.BrightBlack));
        return AlignRight(sb.ToString());
    }
}