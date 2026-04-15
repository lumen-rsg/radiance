using System.Text;

namespace Radiance.Themes.Builtins;

/// <summary>
/// A theme optimized for light terminal backgrounds.
/// </summary>
public sealed class LightTheme : ThemeBase
{
    public override string Name => "light";
    public override string Description => "Optimized for light terminal backgrounds";
    public override string Author => "Radiance";

    public override string RenderPrompt(PromptContext ctx)
    {
        var sb = new StringBuilder();

        // Directory in blue (good contrast on light)
        sb.Append(Color(ctx.TildeCwd, AnsiColor.Blue, bold: true));

        // Git info in magenta
        if (!string.IsNullOrEmpty(ctx.GitBranch))
        {
            sb.Append(' ');
            var gitInfo = ctx.GitBranch;
            if (ctx.GitDirty) gitInfo += "\u25cf";
            sb.Append(Color($"({gitInfo})", AnsiColor.Magenta));
        }

        // User@host dimmed
        sb.Append(' ');
        sb.Append(Color($"{ctx.User}@{ctx.Host}", AnsiColor.BrightBlack));

        // Exit code
        if (ctx.LastExitCode != 0)
        {
            sb.Append(' ');
            sb.Append(Color(ctx.LastExitCode.ToString(), AnsiColor.Red, bold: true));
        }

        // Two-line prompt
        sb.Append('\n');
        sb.Append(Color("\u276f", ctx.LastExitCode == 0 ? AnsiColor.Green : AnsiColor.Red, bold: true));
        sb.Append(' ');

        return sb.ToString();
    }

    public override string RenderRightPrompt(PromptContext ctx)
    {
        var sb = new StringBuilder();
        sb.Append(Color(ctx.Time, AnsiColor.BrightBlack));
        return AlignRight(sb.ToString());
    }
}