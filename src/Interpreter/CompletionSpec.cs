namespace Radiance.Interpreter;

/// <summary>
/// Specifies how tab completion should behave for a given command.
/// </summary>
public sealed class CompletionSpec
{
    /// <summary>
    /// The command name this completion applies to.
    /// </summary>
    public string CommandName { get; init; } = string.Empty;

    /// <summary>
    /// The type of completion to perform.
    /// </summary>
    public CompletionType Type { get; init; }

    /// <summary>
    /// For <see cref="CompletionType.Function"/>: the function name to call.
    /// </summary>
    public string? FunctionName { get; init; }

    /// <summary>
    /// For <see cref="CompletionType.Words"/>: the list of words to complete from.
    /// </summary>
    public List<string>? WordList { get; init; }

    /// <summary>
    /// For <see cref="CompletionType.GlobPattern"/>: the glob pattern to match.
    /// </summary>
    public string? GlobPattern { get; init; }
}

/// <summary>
/// The type of tab completion.
/// </summary>
public enum CompletionType
{
    /// <summary>Default completion (files + commands).</summary>
    Default,

    /// <summary>Complete with command names.</summary>
    Commands,

    /// <summary>Complete with filenames.</summary>
    Files,

    /// <summary>Complete with directory names.</summary>
    Directories,

    /// <summary>Complete by calling a shell function.</summary>
    Function,

    /// <summary>Complete from a word list.</summary>
    Words,

    /// <summary>Complete from a glob pattern.</summary>
    GlobPattern
}
