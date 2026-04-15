using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace Radiance.Expansion;

/// <summary>
/// Performs filename generation (glob expansion) on shell words.
/// Expands patterns containing <c>*</c>, <c>?</c>, and <c>[...]</c> into
/// matching filenames from the filesystem.
/// <para>
/// BASH glob behavior:
/// <list type="bullet">
/// <item><c>*</c> matches any sequence of characters (except leading <c>.</c>)</item>
/// <item><c>?</c> matches any single character (except leading <c>.</c>)</item>
/// <item><c>[abc]</c> matches any character in the set</item>
/// <item><c>[a-z]</c> matches any character in the range</item>
/// <item><c>[!abc]</c> or <c>[^abc]</c> matches any character NOT in the set</item>
/// <item>If no matches found, the original pattern is returned (literal)</item>
/// <item>Files starting with <c>.</c> only match if the pattern explicitly starts with <c>.</c></item>
/// </list>
/// </para>
/// </summary>
public sealed class GlobExpander
{
    /// <summary>
    /// Cache of compiled glob-to-regex patterns to avoid recompilation.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new();

    /// <summary>
    /// Expands glob patterns in the given text, returning one or more matching filenames.
    /// If no glob characters are present or no matches are found, returns a single-element
    /// list containing the original text.
    /// </summary>
    /// <param name="text">The text potentially containing glob patterns.</param>
    /// <returns>A list of expanded filenames, or the original text if no expansion occurred.</returns>
    public static List<string> Expand(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [text];

        // Check if the text contains any glob characters
        if (!ContainsGlobChars(text))
            return [text];

        // Determine the directory and pattern
        string dir;
        string pattern;

        var lastSlash = text.LastIndexOf('/');
        if (lastSlash >= 0)
        {
            var dirPart = text[..(lastSlash + 1)];
            pattern = text[(lastSlash + 1)..];

            // Handle absolute vs relative paths
            dir = dirPart == "/" ? "/" : dirPart.TrimEnd('/');
            if (string.IsNullOrEmpty(dir))
                dir = ".";
        }
        else
        {
            dir = ".";
            pattern = text;
        }

        // If pattern is empty, no glob
        if (string.IsNullOrEmpty(pattern))
            return [text];

        // Check if pattern itself has glob chars
        if (!ContainsGlobChars(pattern))
            return [text];

        try
        {
            var matches = MatchFiles(dir, pattern);

            if (matches.Count == 0)
            {
                // No matches — return original (BASH behavior)
                return [text];
            }

            // Sort matches (BASH default behavior)
            matches.Sort(StringComparer.Ordinal);

            // Prepend the directory path if it was specified
            if (lastSlash >= 0)
            {
                var prefix = text[..(lastSlash + 1)];
                matches = matches.ConvertAll(m => prefix + m);
            }

            return matches;
        }
        catch
        {
            // On error, return original
            return [text];
        }
    }

    /// <summary>
    /// Checks if the text contains any unescaped glob characters (*, ?, [).
    /// </summary>
    internal static bool ContainsGlobChars(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is '*' or '?')
                return true;

            if (text[i] == '[')
            {
                // Check for valid bracket expression [...]
                var j = i + 1;
                if (j < text.Length && text[j] is '!' or '^')
                    j++;
                if (j < text.Length && text[j] == ']')
                    j++;
                while (j < text.Length && text[j] != ']')
                    j++;
                if (j < text.Length)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Matches files in the given directory against the glob pattern.
    /// </summary>
    private static List<string> MatchFiles(string dir, string pattern)
    {
        var matches = new List<string>();

        if (!Directory.Exists(dir))
            return matches;

        var regex = GlobToRegex(pattern);
        var mustMatchDot = pattern.StartsWith('.');

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
            {
                var name = Path.GetFileName(entry);

                // Hidden files only match if pattern explicitly starts with '.'
                if (!mustMatchDot && name.StartsWith('.'))
                    continue;

                if (regex.IsMatch(name))
                {
                    matches.Add(name);
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip directories we can't read
        }
        catch (DirectoryNotFoundException)
        {
            // Directory disappeared
        }

        return matches;
    }

    /// <summary>
    /// Converts a glob pattern to a <see cref="Regex"/> for matching.
    /// </summary>
    private static Regex GlobToRegex(string pattern)
    {
        var sb = new StringBuilder();
        sb.Append('^');

        var i = 0;
        while (i < pattern.Length)
        {
            var c = pattern[i];

            switch (c)
            {
                case '*':
                    sb.Append(".*");
                    break;

                case '?':
                    sb.Append('.');
                    break;

                case '[':
                    // Bracket expression — pass through to regex with minor translation
                    sb.Append('[');
                    i++;

                    if (i < pattern.Length && pattern[i] is '!' or '^')
                    {
                        sb.Append('^');
                        i++;
                    }

                    // First character after [ or [^ can be ] (included in set)
                    if (i < pattern.Length && pattern[i] == ']')
                    {
                        sb.Append(']');
                        i++;
                    }

                    while (i < pattern.Length && pattern[i] != ']')
                    {
                        // Escape regex-special characters inside brackets (except for ranges)
                        var ch = pattern[i];
                        if (ch is '.' or '\\' or '(' or ')' or '{' or '}' or '+' or '^' or '$' or '|' or '*')
                        {
                            sb.Append('\\');
                            sb.Append(ch);
                        }
                        else
                        {
                            sb.Append(ch);
                        }

                        i++;
                    }

                    sb.Append(']');
                    break;

                // Escape regex-special characters
                case '.' or '\\' or '(' or ')' or '{' or '}' or '+' or '^' or '$' or '|':
                    sb.Append('\\');
                    sb.Append(c);
                    break;

                default:
                    sb.Append(c);
                    break;
            }

            i++;
        }

        sb.Append('$');

        var patternStr = sb.ToString();
        return RegexCache.GetOrAdd(pattern, _ => new Regex(patternStr, RegexOptions.None));
    }
}
