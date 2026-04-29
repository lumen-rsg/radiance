using System.Text;

namespace Radiance.Expansion;

/// <summary>
/// Implements BASH brace expansion: {a,b,c}, {1..10}, {a..z}.
/// Brace expansion is the first step in the expansion pipeline and produces
/// multiple words from a single word. It does NOT occur inside double or single quotes.
/// </summary>
public static class BraceExpander
{
    /// <summary>
    /// Expands brace expressions in the given text, returning one or more results.
    /// If no brace expressions are found, returns a single-element list with the original text.
    /// </summary>
    public static List<string> Expand(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('{'))
            return [text];

        var results = new List<string> { "" };
        ExpandInto(results, text, 0, text.Length);
        return results;
    }

    /// <summary>
    /// Recursively expands brace expressions, building up the results list.
    /// </summary>
    private static void ExpandInto(List<string> results, string text, int start, int end)
    {
        var i = start;

        while (i < end)
        {
            if (text[i] == '{')
            {
                // Find matching closing brace
                var braceEnd = FindMatchingBrace(text, i, end);
                if (braceEnd < 0)
                {
                    // No matching brace — treat as literal
                    AppendLiteral(results, '{');
                    i++;
                    continue;
                }

                var inner = text[(i + 1)..braceEnd];

                // Check for sequence expression: {start..end[..step]}
                if (TryParseSequence(inner, out var sequence))
                {
                    // Replace each result with result + each sequence element
                    var newResults = new List<string>();
                    foreach (var prefix in results)
                        foreach (var item in sequence)
                            newResults.Add(prefix + item);
                    results.Clear();
                    results.AddRange(newResults);
                }
                // Check for comma list: {a,b,c}
                else if (inner.Contains(','))
                {
                    var elements = SplitCommaList(inner);
                    var newResults = new List<string>();
                    foreach (var prefix in results)
                        foreach (var element in elements)
                            newResults.Add(prefix + element);
                    results.Clear();
                    results.AddRange(newResults);
                }
                else
                {
                    // No valid brace expression — treat as literal { }
                    foreach (var result in results)
                    {
                        // Already in results, we'll need to append
                    }
                    AppendLiteral(results, '{');
                    i++;
                    continue;
                }

                // Continue with the rest of the string after the closing brace
                i = braceEnd + 1;
            }
            else
            {
                // Regular character — append to all results
                AppendLiteral(results, text[i]);
                i++;
            }
        }
    }

    /// <summary>
    /// Finds the matching closing brace, respecting nesting.
    /// Returns -1 if no matching brace is found.
    /// </summary>
    private static int FindMatchingBrace(string text, int openPos, int limit)
    {
        var depth = 1;
        for (var i = openPos + 1; i < limit; i++)
        {
            if (text[i] == '\\' && i + 1 < limit)
            {
                i++; // skip escaped char
                continue;
            }

            if (text[i] == '{')
                depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Appends a character to all strings in the results list.
    /// </summary>
    private static void AppendLiteral(List<string> results, char c)
    {
        for (var i = 0; i < results.Count; i++)
            results[i] += c;
    }

    /// <summary>
    /// Tries to parse a sequence expression: start..end or start..end..step.
    /// Supports numeric and alphabetic sequences.
    /// </summary>
    private static bool TryParseSequence(string inner, out List<string> sequence)
    {
        sequence = [];

        // Must contain ..
        var dotDot = inner.IndexOf("..", StringComparison.Ordinal);
        if (dotDot < 0)
            return false;

        var startStr = inner[..dotDot];
        var rest = inner[(dotDot + 2)..];

        // Check for step: start..end..step
        string endStr;
        int step = 1;

        var secondDotDot = rest.IndexOf("..", StringComparison.Ordinal);
        if (secondDotDot >= 0)
        {
            endStr = rest[..secondDotDot];
            var stepStr = rest[(secondDotDot + 2)..];
            if (!int.TryParse(stepStr, out step) || step == 0)
                return false;
        }
        else
        {
            endStr = rest;
        }

        if (string.IsNullOrEmpty(startStr) || string.IsNullOrEmpty(endStr))
            return false;

        // Numeric sequence
        if (int.TryParse(startStr, out var startNum) && int.TryParse(endStr, out var endNum))
        {
            if (startNum <= endNum)
            {
                for (var n = startNum; n <= endNum; n += step)
                    sequence.Add(n.ToString());
            }
            else
            {
                for (var n = startNum; n >= endNum; n -= step)
                    sequence.Add(n.ToString());
            }

            // Pad with leading zeros
            if (startStr.StartsWith('0') || endStr.StartsWith('0'))
            {
                var maxLen = Math.Max(startStr.Length, endStr.Length);
                for (var i = 0; i < sequence.Count; i++)
                    sequence[i] = sequence[i].PadLeft(maxLen, '0');
            }

            return sequence.Count > 0;
        }

        // Alphabetic sequence (single chars)
        if (startStr.Length == 1 && endStr.Length == 1 && char.IsLetter(startStr[0]) && char.IsLetter(endStr[0]))
        {
            var startChar = startStr[0];
            var endChar = endStr[0];

            if (char.IsUpper(startChar) && char.IsUpper(endChar))
            {
                if (startChar <= endChar)
                {
                    for (var c = startChar; c <= endChar; c += (char)step)
                        sequence.Add(c.ToString());
                }
                else
                {
                    for (var c = startChar; c >= endChar; c -= (char)step)
                        sequence.Add(c.ToString());
                }
            }
            else
            {
                var s = char.ToLower(startChar);
                var e = char.ToLower(endChar);
                if (s <= e)
                {
                    for (var c = s; c <= e; c += (char)step)
                        sequence.Add(c.ToString());
                }
                else
                {
                    for (var c = s; c >= e; c -= (char)step)
                        sequence.Add(c.ToString());
                }
            }

            return sequence.Count > 0;
        }

        return false;
    }

    /// <summary>
    /// Splits a comma-separated list, respecting nested braces.
    /// </summary>
    private static List<string> SplitCommaList(string inner)
    {
        var elements = new List<string>();
        var current = new StringBuilder();
        var depth = 0;

        foreach (var c in inner)
        {
            if (c == '{')
            {
                depth++;
                current.Append(c);
            }
            else if (c == '}')
            {
                depth--;
                current.Append(c);
            }
            else if (c == ',' && depth == 0)
            {
                // Recursively expand nested braces in each element
                var elem = current.ToString();
                if (elem.Contains('{'))
                {
                    var expanded = Expand(elem);
                    elements.AddRange(expanded);
                }
                else
                {
                    elements.Add(elem);
                }
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        // Add the last element
        var lastElem = current.ToString();
        if (lastElem.Contains('{'))
        {
            var expanded = Expand(lastElem);
            elements.AddRange(expanded);
        }
        else
        {
            elements.Add(lastElem);
        }

        return elements;
    }
}
