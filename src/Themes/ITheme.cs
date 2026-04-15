namespace Radiance.Themes;

/// <summary>
/// Defines the contract for a Radiance shell theme.
/// Themes control the visual appearance of the shell prompt.
/// </summary>
public interface ITheme
{
    /// <summary>
    /// Unique identifier for this theme.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Short description of the theme.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Author of the theme.
    /// </summary>
    string Author { get; }

    /// <summary>
    /// Renders the left prompt string (main prompt).
    /// May include ANSI escape codes for colors and formatting.
    /// </summary>
    string RenderPrompt(PromptContext ctx);

    /// <summary>
    /// Renders the right prompt string (RPROMPT).
    /// Return empty string if not supported.
    /// May include ANSI escape codes for colors and formatting.
    /// </summary>
    string RenderRightPrompt(PromptContext ctx);
}