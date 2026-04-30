using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Radiance.Interpreter;

namespace Radiance.Expansion;

/// <summary>
/// Performs filename generation (glob expansion) on shell words.
/// Expands patterns containing <c>*</c>, <c>?</c>, and <c>[...]</c> into
/// matching filenames from the filesystem.
/// With extglob enabled, also supports extended glob patterns:
/// <c>?(pattern)</c>, <c>*(pattern)</c>, <c>+(pattern)</c>, <c>@(pattern)</c>, <c>!(pattern)</c>.
/// </summary>
public sealed class GlobExpander
{
    /// <summary>
    /// Cache of compiled glob-to-regex patterns to avoid recompilation.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new();
    private const int MaxRegexCacheSize = 64;

    /// <summary>
    /// Expands glob patterns in the given text, returning one or more matching filenames.
    /// If no glob characters are present or no matches are found, returns a single-element
    /// list containing the original text.
    /// </summary>
    /// <param name="text">The text potentially containing glob patterns.</param>
    /// <param name="options">Shell options (for extglob support). Null = no extglob.</param>
    /// <returns>A list of expanded filenames, or the original text if no expansion occurred.</returns>
    public static List<string> Expand(string text, ShellOptions? options = null)
    {
        if (string.IsNullOrEmpty(text))
            return [text];

        // Check if the text contains any glob characters
        if (!ContainsGlobChars(text) && !(options?.ExtGlob == true && ContainsExtGlobChars(text)))
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
        if (!ContainsGlobChars(pattern) && !(options?.ExtGlob == true && ContainsExtGlobChars(pattern)))
            return [text];

        try
        {
            var matches = MatchFiles(dir, pattern, options);

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
    private static List<string> MatchFiles(string dir, string pattern, ShellOptions? options = null)
    {
        var matches = new List<string>();

        if (!Directory.Exists(dir))
            return matches;

        var regex = GlobToRegex(pattern, options);
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
    /// Checks if the text contains extended glob characters (extglob patterns).
    /// Extglob patterns: ?(...), *(...), +(...), @(...), !(...)
    /// </summary>
    internal static bool ContainsExtGlobChars(string text)
    {
        for (var i = 0; i + 1 < text.Length; i++)
        {
            if (text[i] is '?' or '*' or '+' or '@' or '!')
            {
                if (text[i + 1] == '(')
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Converts a glob pattern to a <see cref="Regex"/> for matching.
    /// Supports extended glob patterns when extglob is enabled.
    /// </summary>
    private static Regex GlobToRegex(string pattern, ShellOptions? options = null)
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

                // Extglob patterns: ?(...), *(...), +(...), @(...), !(...)
                case '?' or '*' or '+' or '@' or '!' when options?.ExtGlob == true && i + 1 < pattern.Length && pattern[i + 1] == '(':
                {
                    // Find the matching closing paren
                    var parenDepth = 1;
                    var j = i + 2;
                    while (j < pattern.Length && parenDepth > 0)
                    {
                        if (pattern[j] == '(') parenDepth++;
                        else if (pattern[j] == ')') parenDepth--;
                        if (parenDepth > 0) j++;
                    }

                    var innerPattern = pattern[(i + 2)..j];
                    var innerRegex = ConvertInnerPattern(innerPattern);

                    switch (c)
                    {
                        case '?': // Zero or one occurrence
                            sb.Append($"({innerRegex})?");
                            break;
                        case '*': // Zero or more occurrences
                            sb.Append($"({innerRegex})*");
                            break;
                        case '+': // One or more occurrences
                            sb.Append($"({innerRegex})+");
                            break;
                        case '@': // Exactly one occurrence
                            sb.Append($"({innerRegex})");
                            break;
                        case '!': // Match anything except
                            sb.Append($"(?:(?!{innerRegex}).)*");
                            break;
                    }

                    i = j; // Skip past closing paren
                    break;
                }

                default:
                    sb.Append(c);
                    break;
            }

            i++;
        }

        sb.Append('$');

        var patternStr = sb.ToString();
        var cacheKey = options?.ExtGlob == true ? $"ext:{pattern}" : pattern;
        var regex = RegexCache.GetOrAdd(cacheKey, _ => new Regex(patternStr, RegexOptions.None));
        if (RegexCache.Count > MaxRegexCacheSize)
        {
            foreach (var key in RegexCache.Keys.Take(RegexCache.Count / 2).ToList())
                RegexCache.TryRemove(key, out _);
        }
        return regex;
    }

    /// <summary>
    /// Converts the inner pattern of an extglob expression.
    /// Handles pipe-separated alternatives: (a|b|c) → a|b|c
    /// </summary>
    private static string ConvertInnerPattern(string inner)
    {
        // Split by | for alternation, convert each part as a glob pattern
        var parts = inner.Split('|');
        var converted = parts.Select(p =>
        {
            // Simple conversion: escape regex-special chars except glob chars
            var sb = new StringBuilder();
            foreach (var c in p)
            {
                switch (c)
                {
                    case '*':
                        sb.Append(".*");
                        break;
                    case '?':
                        sb.Append('.');
                        break;
                    case '.' or '\\' or '(' or ')' or '{' or '}' or '+' or '^' or '$' or '|':
                        sb.Append('\\');
                        sb.Append(c);
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        });
        return string.Join("|", converted);
    }
}
