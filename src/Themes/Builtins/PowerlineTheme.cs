using System.Text;

namespace Radiance.Themes.Builtins;

/// <summary>
/// Powerline-inspired theme with segment separators.
/// </summary>
public sealed class PowerlineTheme : ThemeBase
{
    private const string Sep = "\ue0b0";      // Right-pointing solid arrow
    private const string SepThin = "\ue0b1";  // Right-pointing thin arrow

    public override string Name => "powerline";
    public override string Description => "Powerline-style prompt with colored segments";
    public override string Author => "Radiance";

    public override string RenderPrompt(PromptContext ctx)
    {
        var sb = new StringBuilder();

        // Segment 1: user@host
        var userBg = ctx.IsRoot ? AnsiColor.Red : AnsiColor.BrightGreen;
        var userFg = ctx.IsRoot ? AnsiColor.White : AnsiColor.Black;

        sb.Append(Bg(userBg));
        sb.Append(Fg(userFg));
        sb.Append(Bold);
        sb.Append($" {ctx.User}@{ctx.Host} ");
        sb.Append(Reset);

        // Separator: transition from userBg to Blue bg
        sb.Append(Fg(AnsiColor.BrightBlue));
        sb.Append(Bg(userBg));
        sb.Append(Sep);
        sb.Append(Reset);

        // Segment 2: CWD (white on bright_blue bg)
        sb.Append(Bg(AnsiColor.BrightBlue));
        sb.Append(Fg(AnsiColor.White));
        sb.Append(Bold);
        sb.Append($" {ctx.TildeCwd} ");
        sb.Append(Reset);

        // Git segment if available
        if (!string.IsNullOrEmpty(ctx.GitBranch))
        {
            var gitBg = ctx.GitDirty ? AnsiColor.BrightRed : AnsiColor.BrightMagenta;
            sb.Append(Fg(gitBg));
            sb.Append(Bg(AnsiColor.BrightBlue));
            sb.Append(Sep);
            sb.Append(Reset);

            var gitText = ctx.GitBranch;
            if (ctx.GitDirty) gitText += " \u2717";

            sb.Append(Bg(gitBg));
            sb.Append(Fg(AnsiColor.White));
            sb.Append(Bold);
            sb.Append($" {gitText} ");
            sb.Append(Reset);

            sb.Append(Fg(gitBg));
            sb.Append(Sep);
            sb.Append(Reset);
        }
        else
        {
            sb.Append(Fg(AnsiColor.BrightBlue));
            sb.Append(Sep);
            sb.Append(Reset);
        }

        // Newline + prompt character
        sb.Append('\n');
        sb.Append(FormatPromptChar(ctx, "\u276f", AnsiColor.BrightGreen, AnsiColor.BrightRed));
        sb.Append(' ');

        return sb.ToString();
    }

    public override string RenderRightPrompt(PromptContext ctx)
    {
        var text = Color($" {ctx.Time} ", AnsiColor.White, AnsiColor.BrightBlack);
        return AlignRight(text);
    }
}