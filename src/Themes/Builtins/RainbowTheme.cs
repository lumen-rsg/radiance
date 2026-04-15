using System.Text;

namespace Radiance.Themes.Builtins;

/// <summary>
/// A colorful multi-segment theme with distinct colors for each element.
/// </summary>
public sealed class RainbowTheme : ThemeBase
{
    public override string Name => "rainbow";
    public override string Description => "Colorful multi-segment theme with vibrant colors";
    public override string Author => "Radiance";

    public override string RenderPrompt(PromptContext ctx)
    {
        var sb = new StringBuilder();

        // User in bright green
        sb.Append(Color(ctx.User, AnsiColor.BrightGreen, bold: true));
        sb.Append(Color("@", AnsiColor.White));
        sb.Append(Color(ctx.Host, AnsiColor.BrightCyan));
        sb.Append(Color(":", AnsiColor.White));
        sb.Append(Color(ctx.TildeCwd, AnsiColor.BrightYellow, bold: true));

        // Git branch
        if (!string.IsNullOrEmpty(ctx.GitBranch))
        {
            sb.Append(' ');
            var branch = ctx.GitBranch;
            if (ctx.GitDirty)
                sb.Append(Color($" [{branch}]", AnsiColor.BrightRed, bold: true));
            else
                sb.Append(Color($" [{branch}]", AnsiColor.BrightMagenta));
        }

        // Jobs indicator
        if (ctx.JobCount > 0)
        {
            sb.Append(' ');
            sb.Append(Color($"[{ctx.JobCount}job{(ctx.JobCount > 1 ? "s" : "")}]",
                AnsiColor.BrightRed, bold: true));
        }

        // Exit code indicator
        if (ctx.LastExitCode != 0)
        {
            sb.Append(' ');
            sb.Append(Color($"[{ctx.LastExitCode}]", AnsiColor.BrightRed, bold: true));
        }

        // Prompt char with arrow
        sb.Append('\n');
        sb.Append(FormatPromptChar(ctx, "\u279c", AnsiColor.BrightGreen, AnsiColor.BrightRed));
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