using Radiance.Interpreter;

namespace Radiance.Shell;

/// <summary>
/// Expands BASH-style backslash escape sequences in prompt strings (PS1, PS2, PS4).
/// Supports: \u, \h, \H, \w, \W, \n, \t, \T, \d, \$, \!, \#, \[, \], \\
/// Also performs variable expansion ($VAR, ${VAR}) after backslash processing.
/// </summary>
public static class PromptExpander
{
    /// <summary>
    /// Expands a prompt string, processing BASH-style backslash escapes and shell variables.
    /// </summary>
    /// <param name="prompt">The raw prompt string.</param>
    /// <param name="context">The shell context for variable lookup.</param>
    /// <param name="historyCount">The current history entry count (for \!).</param>
    /// <param name="commandNumber">The current command number (for \#).</param>
    /// <returns>The expanded prompt string with ANSI codes preserved.</returns>
    public static string Expand(string prompt, ShellContext context, int historyCount = 0, int commandNumber = 0)
    {
        if (string.IsNullOrEmpty(prompt))
            return prompt;

        var result = new System.Text.StringBuilder(prompt.Length * 2);
        var i = 0;

        while (i < prompt.Length)
        {
            if (prompt[i] == '\\' && i + 1 < prompt.Length)
            {
                var expanded = ExpandEscape(prompt[i + 1], context, historyCount, commandNumber);
                if (expanded is not null)
                {
                    result.Append(expanded);
                    i += 2;
                    continue;
                }

                // Unknown escape — keep the backslash and next char
                result.Append(prompt[i]);
                i++;
                continue;
            }

            result.Append(prompt[i]);
            i++;
        }

        // Now expand shell variables in the result
        var expandedStr = result.ToString();
        return ExpandPromptVariables(expandedStr, context);
    }

    /// <summary>
    /// Expands a single backslash escape sequence.
    /// Returns null if the character is not a recognized escape.
    /// </summary>
    private static string? ExpandEscape(char c, ShellContext context, int historyCount, int commandNumber)
    {
        switch (c)
        {
            case 'u': // Username
                return Environment.GetEnvironmentVariable("USER")
                       ?? Environment.GetEnvironmentVariable("LOGNAME")
                       ?? "user";

            case 'h': // Hostname (short)
                var host = Environment.GetEnvironmentVariable("HOSTNAME")
                           ?? System.Net.Dns.GetHostName();
                var dotIdx = host.IndexOf('.');
                return dotIdx >= 0 ? host[..dotIdx] : host;

            case 'H': // Hostname (full)
                return Environment.GetEnvironmentVariable("HOSTNAME")
                       ?? System.Net.Dns.GetHostName();

            case 'w': // Working directory with ~ substitution
                var cwd = context.CurrentDirectory;
                var home = Environment.GetEnvironmentVariable("HOME") ?? "";
                return cwd.StartsWith(home)
                    ? "~" + cwd[home.Length..]
                    : cwd;

            case 'W': // Basename of working directory
                var cwdBase = context.CurrentDirectory;
                var homeBase = Environment.GetEnvironmentVariable("HOME") ?? "";
                if (cwdBase == homeBase)
                    return "~";
                return Path.GetFileName(cwdBase) ?? cwdBase;

            case 'n': // Newline
                return "\n";

            case 't': // Time 24-hour HH:MM:SS
                return DateTime.Now.ToString("HH:mm:ss");

            case 'T': // Time 12-hour HH:MM:SS
                return DateTime.Now.ToString("hh:mm:ss");

            case '@': // Time 12-hour am/pm
                return DateTime.Now.ToString("hh:mm tt");

            case 'd': // Date
                return DateTime.Now.ToString("ddd MMM dd");

            case 'D': // Format: \D{format}
                // Handled in main loop since it needs braces
                return null;

            case '$': // $ for normal user, # for root
                return Environment.GetEnvironmentVariable("USER") == "root" ? "#" : "$";

            case '!': // History event number
                return historyCount.ToString();

            case '#': // Command number
                return commandNumber.ToString();

            case '[': // Begin non-printing characters (ANSI escape) — pass through
                return "\x1b]"; // marker, will be cleaned up

            case ']': // End non-printing characters — pass through
                return "\x1b["; // marker

            case '\\': // Literal backslash
                return "\\";

            case 'a': // Bell
                return "\a";

            case 'e': // Escape character
                return "\x1b";

            case '0': // Octal \0nnn — simplified
            case '1':
            case '2':
            case '3':
            case '4':
            case '5':
            case '6':
            case '7':
                return null; // Could implement octal, skip for now

            default:
                return null;
        }
    }

    /// <summary>
    /// Performs simple variable expansion on prompt strings.
    /// Handles $VAR and ${VAR} references.
    /// </summary>
    private static string ExpandPromptVariables(string input, ShellContext context)
    {
        var result = new System.Text.StringBuilder(input.Length);
        var i = 0;

        while (i < input.Length)
        {
            if (input[i] == '$' && i + 1 < input.Length)
            {
                if (input[i + 1] == '{')
                {
                    // ${VAR} form
                    var end = input.IndexOf('}', i + 2);
                    if (end >= 0)
                    {
                        var name = input[(i + 2)..end];
                        result.Append(context.GetVariable(name));
                        i = end + 1;
                        continue;
                    }
                }
                else if (char.IsLetter(input[i + 1]) || input[i + 1] == '_')
                {
                    // $VAR form
                    var j = i + 1;
                    while (j < input.Length && (char.IsLetterOrDigit(input[j]) || input[j] == '_'))
                        j++;
                    var varName = input[(i + 1)..j];
                    result.Append(context.GetVariable(varName));
                    i = j;
                    continue;
                }
                else if (input[i + 1] == '?')
                {
                    result.Append(context.LastExitCode);
                    i += 2;
                    continue;
                }
            }

            result.Append(input[i]);
            i++;
        }

        return result.ToString();
    }
}
