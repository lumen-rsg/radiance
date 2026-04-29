using System.Text;

namespace Radiance.Shell;

/// <summary>
/// Preprocesses here-document constructs in shell input before lexing/parsing.
/// Detects <c>&lt;&lt;DELIM</c>, <c>&lt;&lt;-DELIM</c>, and <c>&lt;&lt;&lt;word</c>
/// patterns, extracts the heredoc body, writes it to a temporary file,
/// and replaces the construct with a <c>&lt; tempfile</c> redirect.
/// <para>
/// This approach reuses the existing file-redirect infrastructure without
/// requiring parser or interpreter changes.
/// </para>
/// </summary>
public static class HeredocProcessor
{
    /// <summary>
    /// Processes heredoc constructs in the input string.
    /// Returns the processed input (with heredocs replaced by file redirects)
    /// and a list of temporary file paths that should be cleaned up after execution.
    /// </summary>
    /// <param name="input">The raw shell input potentially containing heredocs.</param>
    /// <returns>The processed input and list of temp files to clean up.</returns>
    public static (string input, List<string> tempFiles) Process(string input)
    {
        var tempFiles = new List<string>();
        var result = new StringBuilder();
        var i = 0;

        while (i < input.Length)
        {
            // Skip single-quoted strings
            if (input[i] == '\'')
            {
                result.Append(input[i]);
                i++;
                while (i < input.Length && input[i] != '\'')
                {
                    result.Append(input[i]);
                    i++;
                }
                if (i < input.Length)
                {
                    result.Append(input[i]);
                    i++;
                }
                continue;
            }

            // Skip double-quoted strings
            if (input[i] == '"')
            {
                result.Append(input[i]);
                i++;
                while (i < input.Length && input[i] != '"')
                {
                    if (input[i] == '\\' && i + 1 < input.Length)
                    {
                        result.Append(input[i]);
                        i++;
                    }
                    result.Append(input[i]);
                    i++;
                }
                if (i < input.Length)
                {
                    result.Append(input[i]);
                    i++;
                }
                continue;
            }

            // Skip backslash-escaped characters
            if (input[i] == '\\' && i + 1 < input.Length)
            {
                result.Append(input[i]);
                result.Append(input[i + 1]);
                i += 2;
                continue;
            }

            // Check for <<< (here-string)
            if (input[i] == '<' && i + 2 < input.Length && input[i + 1] == '<' && input[i + 2] == '<')
            {
                i += 3;
                // Skip whitespace
                while (i < input.Length && (input[i] == ' ' || input[i] == '\t'))
                    i++;

                // Read the word (until newline, space, or end)
                var word = new StringBuilder();
                while (i < input.Length && input[i] != '\n' && input[i] != ' ' && input[i] != '|'
                       && input[i] != ';' && input[i] != '&' && input[i] != '>')
                {
                    word.Append(input[i]);
                    i++;
                }

                // Write the word + newline to a temp file
                var content = word.ToString() + "\n";
                var tempFile = WriteTempFile(content);
                tempFiles.Add(tempFile);
                result.Append($"< {tempFile}");
                continue;
            }

            // Check for <<- (heredoc with tab stripping)
            if (input[i] == '<' && i + 2 < input.Length && input[i + 1] == '<' && input[i + 2] == '-')
            {
                i += 3;
                var (delimiter, quoted) = ReadDelimiter(input, ref i);
                var body = ReadHeredocBody(input, ref i, delimiter, stripTabs: true);
                var tempFile = WriteTempFile(body);
                tempFiles.Add(tempFile);
                result.Append($"< {tempFile}");
                continue;
            }

            // Check for << (standard heredoc)
            if (input[i] == '<' && i + 1 < input.Length && input[i + 1] == '<'
                && (i + 2 >= input.Length || input[i + 2] != '<'))  // not <<<
            {
                i += 2;
                var (delimiter, quoted) = ReadDelimiter(input, ref i);
                var body = ReadHeredocBody(input, ref i, delimiter, stripTabs: false);
                var tempFile = WriteTempFile(body);
                tempFiles.Add(tempFile);
                result.Append($"< {tempFile}");
                continue;
            }

            result.Append(input[i]);
            i++;
        }

        return (result.ToString(), tempFiles);
    }

    /// <summary>
    /// Reads the heredoc delimiter word after &lt;&lt; or &lt;&lt;-.
    /// Handles quoted delimiters: &lt;&lt;'EOF', &lt;&lt;"EOF", &lt;&lt;\EOF.
    /// </summary>
    private static (string delimiter, bool quoted) ReadDelimiter(string input, ref int i)
    {
        // Skip whitespace
        while (i < input.Length && (input[i] == ' ' || input[i] == '\t'))
            i++;

        if (i >= input.Length)
            return ("", false);

        var quoted = false;
        var sb = new StringBuilder();

        // Check for quoted delimiter
        if (input[i] == '\'' || input[i] == '"')
        {
            quoted = true;
            var quoteChar = input[i];
            i++;
            while (i < input.Length && input[i] != quoteChar)
            {
                sb.Append(input[i]);
                i++;
            }
            if (i < input.Length)
                i++; // skip closing quote
        }
        else if (input[i] == '\\')
        {
            // \DELIM is treated as quoted
            quoted = true;
            i++;
            while (i < input.Length && !char.IsWhiteSpace(input[i]) && input[i] != ';'
                   && input[i] != '|' && input[i] != '&' && input[i] != '<' && input[i] != '>')
            {
                sb.Append(input[i]);
                i++;
            }
        }
        else
        {
            // Unquoted delimiter — read until whitespace or operator
            while (i < input.Length && !char.IsWhiteSpace(input[i]) && input[i] != ';'
                   && input[i] != '|' && input[i] != '&' && input[i] != '<' && input[i] != '>')
            {
                sb.Append(input[i]);
                i++;
            }
        }

        return (sb.ToString(), quoted);
    }

    /// <summary>
    /// Reads the heredoc body from the input, starting from the current position.
    /// The body starts after the next newline and ends when a line matches the delimiter exactly.
    /// If <paramref name="stripTabs"/> is true, leading tabs are stripped from each line.
    /// </summary>
    private static string ReadHeredocBody(string input, ref int i, string delimiter, bool stripTabs)
    {
        // Skip to the next newline (the rest of the current command line may have pipes, etc.)
        while (i < input.Length && input[i] != '\n')
            i++;

        if (i < input.Length)
            i++; // skip the newline

        var body = new StringBuilder();
        var lineStart = i;

        while (i < input.Length)
        {
            // Find the end of the current line
            lineStart = i;
            while (i < input.Length && input[i] != '\n')
                i++;

            var line = input[lineStart..i];
            if (i < input.Length)
                i++; // skip newline

            // Strip leading tabs for <<-
            if (stripTabs)
                line = line.TrimStart('\t');

            // Check if this line matches the delimiter exactly
            if (line == delimiter)
                break;

            // Append the line to the body
            if (body.Length > 0)
                body.Append('\n');
            body.Append(line);
        }

        body.Append('\n');
        return body.ToString();
    }

    /// <summary>
    /// Writes content to a temporary file and returns the file path.
    /// </summary>
    private static string WriteTempFile(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "radiance-heredoc");
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, $"heredoc_{Guid.NewGuid():N}");
        File.WriteAllText(tempFile, content);
        return tempFile;
    }

    /// <summary>
    /// Cleans up temporary files created by heredoc processing.
    /// </summary>
    /// <param name="tempFiles">The list of temp file paths to delete.</param>
    public static void Cleanup(List<string> tempFiles)
    {
        foreach (var file in tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch { /* ignore cleanup errors */ }
        }
    }
}
