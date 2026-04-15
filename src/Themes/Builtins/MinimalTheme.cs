using System.Text;

namespace Radiance.Themes.Builtins;

/// <summary>
/// A minimal theme with just an arrow and the directory name.
/// </summary>
public sealed class MinimalTheme : ThemeBase
{
    public override string Name => "minimal";
    public override string Description => "Clean minimal prompt with arrow and directory";
    public override string Author => "Radiance";

    public override string RenderPrompt(PromptContext ctx)
    {
        var sb = new StringBuilder();

        // Short directory name
        sb.Append(Color(ctx.ShortCwd, AnsiColor.BrightCyan));

        // Git branch (compact)
        if (!string.IsNullOrEmpty(ctx.GitBranch))
        {
            sb.Append(' ');
            var branch = ctx.GitBranch;
            if (ctx.GitDirty) branch += "*";
            sb.Append(Color($"({branch})", AnsiColor.BrightYellow));
        }

        // Arrow prompt char
        sb.Append(' ');
        var arrowColor = ctx.LastExitCode == 0 ? AnsiColor.BrightMagenta : AnsiColor.BrightRed;
        sb.Append(Color("❯", arrowColor, bold: true));
        sb.Append(' ');

        return sb.ToString();
    }

    public override string RenderRightPrompt(PromptContext ctx) => "";
}