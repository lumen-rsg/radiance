using System.Text;
using Radiance.Interpreter;

namespace Radiance.Expansion;

/// <summary>
/// Performs variable expansion on shell words, replacing <c>$VAR</c>, <c>${VAR}</c>,
/// and special variable references with their values.
/// <para>
/// Supports:
/// <list type="bullet">
/// <item><c>$VAR</c> / <c>${VAR}</c> — shell/environment variables</item>
/// <item><c>$0</c> — shell name or script path</item>
/// <item><c>$1</c>-<c>$9</c> — positional parameters</item>
/// <item><c>$#</c> — count of positional parameters</item>
/// <item><c>$@</c> / <c>$*</c> — all positional parameters</item>
/// <item><c>$?</c> — last exit code</item>
/// <item><c>$$</c> — shell PID</item>
/// <item><c>$!</c> — PID of last background process</item>
/// <item><c>$-</c> — shell options</item>
/// </list>
/// </para>
/// </summary>
public sealed class VariableExpander
{
    /// <summary>
    /// Expands variable references in the given text.
    /// </summary>
    /// <param name="text">The text potentially containing variable references.</param>
    /// <param name="context">The shell execution context.</param>
    /// <returns>The text with all variable references expanded.</returns>
    public static string Expand(string text, ShellContext context)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('$'))
            return text;

        var sb = new StringBuilder();
        var i = 0;

        while (i < text.Length)
        {
            if (text[i] == '$' && i + 1 < text.Length)
            {
                var (expanded, consumed) = ExpandDollar(text, i, context);
                sb.Append(expanded);
                i += consumed;
            }
            else
            {
                sb.Append(text[i]);
                i++;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Expands a single <c>$...</c> reference starting at the given position.
    /// Returns the expanded value and the number of characters consumed.
    /// </summary>
    internal static (string value, int consumed) ExpandDollar(string text, int start, ShellContext context)
    {
        var i = start + 1; // skip $

        if (i >= text.Length)
            return ("$", 1);

        var c = text[i];

        switch (c)
        {
            case '?':
                // $? — last exit code
                return (context.LastExitCode.ToString(), 2);

            case '$':
                // $$ — shell PID
                return (Environment.ProcessId.ToString(), 2);

            case '!':
                // $! — last background PID
                return (context.LastBackgroundPid.ToString(), 2);

            case '#':
                // $# — positional parameter count
                return (context.PositionalParamCount.ToString(), 2);

            case '@':
                // $@ — all positional parameters, space-separated
                return (string.Join(" ", context.PositionalParams), 2);

            case '*':
                // $* — all positional parameters, space-separated
                return (string.Join(" ", context.PositionalParams), 2);

            case '-':
                // $- — shell options
                return (context.ShellOptions, 2);

            case '0':
                // $0 — shell name
                return (context.ShellName, 2);

            case >= '1' and <= '9':
            {
                // Check for multi-digit positional params ($10, $11, etc.)
                // In BASH, $10 is ${1}0, not ${10}. Only single digit.
                return (context.GetPositionalParam(c - '0'), 2);
            }

            case '{':
            {
                // ${VAR} form — could be ${VAR}, ${VAR:-default}, etc.
                var (value, consumed) = ExpandBracedVariable(text, i + 1, context);
                return (value, consumed + 2); // +2 for ${ and }
            }

            case '(' when i + 1 < text.Length && text[i + 1] == '(':
            {
                // $((expr)) — arithmetic expansion (handled by ArithmeticExpander)
                // Return as-is here; the expander pipeline handles it
                return ("$(", 2);
            }

            case '(':
                // $(...) — command substitution (handled by CommandSubstitution)
                // Return as-is here; the expander pipeline handles it
                return ("$(", 2);

            default:
            {
                if (char.IsLetter(c) || c == '_')
                {
                    // $VAR form — read identifier
                    var name = new StringBuilder();
                    var j = i;
                    while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '_'))
                    {
                        name.Append(text[j]);
                        j++;
                    }

                    return (context.GetVariable(name.ToString()), j - start);
                }

                // Lone $ or $<unknown> — keep the $
                return ("$", 1);
            }
        }
    }

    /// <summary>
    /// Expands a braced variable reference <c>${...}</c> starting after the <c>{</c>.
    /// Returns the expanded value and the number of characters consumed (between { and }).
    /// </summary>
    private static (string value, int consumed) ExpandBracedVariable(string text, int start, ShellContext context)
    {
        var i = start;
        var name = new StringBuilder();

        // Read the variable name
        while (i < text.Length && text[i] != '}' && text[i] != ':' && text[i] != '=' && text[i] != '+' && text[i] != '#')
        {
            name.Append(text[i]);
            i++;
        }

        var varName = name.ToString();
        var innerConsumed = i - start;

        // Check for parameter expansion operators: ${VAR:-default}, ${VAR:=default}, ${VAR:+alt}, ${VAR##pattern}, etc.
        if (i < text.Length && text[i] != '}')
        {
            var op = text[i];

            // Handle ${#VAR} — string length
            if (op == '#' && varName == "")
            {
                // ${#VAR} — length of VAR's value
                name.Clear();
                i++; // skip #
                while (i < text.Length && text[i] != '}')
                {
                    name.Append(text[i]);
                    i++;
                }

                // skip closing }
                var lenVarName = name.ToString();
                var lenValue = context.GetVariable(lenVarName);
                return (lenValue.Length.ToString(), i - start + 1);
            }

            // ${VAR:-default} — use default if unset or empty
            if (op == ':' && i + 1 < text.Length && text[i + 1] == '-')
            {
                i += 2; // skip :-
                var defaultValue = ReadUntilBrace(text, i);
                var currentValue = context.GetVariable(varName);
                var result = string.IsNullOrEmpty(currentValue) ? defaultValue : currentValue;
                return (result, i - start + defaultValue.Length + 1);
            }

            // ${VAR:=default} — assign default if unset or empty
            if (op == ':' && i + 1 < text.Length && text[i + 1] == '=')
            {
                i += 2; // skip :=
                var defaultValue = ReadUntilBrace(text, i);
                var currentValue = context.GetVariable(varName);
                if (string.IsNullOrEmpty(currentValue))
                {
                    context.SetVariable(varName, defaultValue);
                    return (defaultValue, i - start + defaultValue.Length + 1);
                }

                return (currentValue, i - start + defaultValue.Length + 1);
            }

            // ${VAR:+alternative} — use alternative if set and non-empty
            if (op == ':' && i + 1 < text.Length && text[i + 1] == '+')
            {
                i += 2; // skip :+
                var altValue = ReadUntilBrace(text, i);
                var currentValue = context.GetVariable(varName);
                var result = !string.IsNullOrEmpty(currentValue) ? altValue : "";
                return (result, i - start + altValue.Length + 1);
            }

            // ${VAR:-...} variant without colon
            if (op == '-')
            {
                i += 1; // skip -
                var defaultValue = ReadUntilBrace(text, i);
                var currentValue = context.GetVariable(varName);
                var result = string.IsNullOrEmpty(currentValue) ? defaultValue : currentValue;
                return (result, i - start + defaultValue.Length);
            }
        }

        // Skip to closing brace if not already there
        while (i < text.Length && text[i] != '}')
            i++;

        // Simple ${VAR} — just expand
        return (context.GetVariable(varName), i - start + 1);
    }

    /// <summary>
    /// Reads text from the given position until the closing <c>}</c>.
    /// Handles nested braces.
    /// </summary>
    private static string ReadUntilBrace(string text, int start)
    {
        var sb = new StringBuilder();
        var depth = 1;
        var i = start;

        while (i < text.Length && depth > 0)
        {
            if (text[i] == '{')
                depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0)
                    break;
            }

            sb.Append(text[i]);
            i++;
        }

        return sb.ToString();
    }
}