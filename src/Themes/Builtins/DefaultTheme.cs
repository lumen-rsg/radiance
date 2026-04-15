using System;
using System.Text;

namespace Radiance.Themes.Builtins;

/// <summary>
/// The default Radiance theme. Matches the classic bash-like prompt style.
/// Format: user@host cwd [git] $
/// </summary>
public sealed class DefaultTheme : ThemeBase
{
    public override string Name => "default";
    public override string Description => "Classic bash-style prompt (user@host cwd $)";
    public override string Author => "Radiance";

    public override string RenderPrompt(PromptContext ctx)
    {
        var sb = new StringBuilder();

        // user@host in green/cyan
        sb.Append(Color($"{ctx.User}", AnsiColor.BrightGreen, bold: true));
        sb.Append(Color("@", AnsiColor.White));
        sb.Append(Color(ctx.Host, AnsiColor.BrightCyan));

        // Space + CWD in blue
        sb.Append(' ');
        sb.Append(Color(ctx.TildeCwd, AnsiColor.BrightBlue, bold: true));

        // Git branch if available
        var git = FormatGit(ctx, prefix: " ", suffix: "");
        if (!string.IsNullOrEmpty(git))
        {
            sb.Append(' ');
            sb.Append(Color(git, AnsiColor.BrightMagenta));
        }

        // Prompt character
        sb.Append(' ');
        sb.Append(FormatPromptChar(ctx));

        sb.Append(' ');
        return sb.ToString();
    }

    public override string RenderRightPrompt(PromptContext ctx) => "";
}