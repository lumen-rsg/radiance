namespace Radiance.Utils;

/// <summary>
/// Tracks whether Console.Out has been redirected for output capture
/// (command substitution, pipeline builtin capture). Uses a [ThreadStatic]
/// counter so nested captures work correctly and no runtime type-checking
/// of Console.Out is required.
/// </summary>
internal static class OutputCapture
{
    [ThreadStatic]
    private static int _depth;

    /// <summary>True when Console.Out has been redirected to a capture writer.</summary>
    internal static bool IsCapturing => _depth > 0;

    /// <summary>Call before <see cref="Console.SetOut"/> for capture.</summary>
    internal static void Push() => _depth++;

    /// <summary>Call after restoring the original <see cref="Console.Out"/>.</summary>
    internal static void Pop() { if (_depth > 0) _depth--; }
}