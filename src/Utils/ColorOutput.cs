using System.IO;

namespace Radiance.Utils;

/// <summary>
/// Provides colorized console output helpers for error messages, warnings,
/// and informational output. Uses ANSI escape codes for terminal coloring.
/// </summary>
public static class ColorOutput
{
    // ANSI escape codes
    private const string Reset = "\x1b[0m";
    private const string Red = "\x1b[31m";
    private const string Yellow = "\x1b[33m";
    private const string Bold = "\x1b[1m";
    private const string Cyan = "\x1b[36m";
    private const string Green = "\x1b[32m";

    /// <summary>
    /// Writes a colorized error message to stderr.
    /// Format: <c>radiance: error: message</c>
    /// </summary>
    /// <param name="message">The error message.</param>
    public static void WriteError(string message)
    {
        Console.Error.WriteLine($"{Red}{Bold}radiance: error:{Reset} {message}");
    }

    /// <summary>
    /// Writes a colorized warning message to stderr.
    /// Format: <c>radiance: warning: message</c>
    /// </summary>
    /// <param name="message">The warning message.</param>
    public static void WriteWarning(string message)
    {
        Console.Error.WriteLine($"{Yellow}radiance: warning:{Reset} {message}");
    }

    /// <summary>
    /// Writes a colorized informational message to stdout.
    /// </summary>
    /// <param name="message">The informational message.</param>
    public static void WriteInfo(string message)
    {
        Console.WriteLine($"{Cyan}radiance:{Reset} {message}");
    }

    /// <summary>
    /// Writes a plain error message to stderr without color (for scripts/redirected output).
    /// Format: <c>radiance: script: line N: message</c>
    /// </summary>
    /// <param name="scriptName">The script file name or context.</param>
    /// <param name="line">The line number (0 if unknown).</param>
    /// <param name="message">The error message.</param>
    public static void WriteScriptError(string scriptName, int line, string message)
    {
        if (line > 0)
        {
            Console.Error.WriteLine($"{Red}{Bold}radiance: {scriptName}: line {line}:{Reset} {message}");
        }
        else
        {
            Console.Error.WriteLine($"{Red}{Bold}radiance: {scriptName}:{Reset} {message}");
        }
    }
}