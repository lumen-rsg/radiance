using System.Text;
using DaVinci.Core;
using DaVinci.Terminal;

namespace DaVinci.Components;

public sealed record CodeBlockProps : ComponentProps
{
    public string Code { get; init; } = "";
    public string Language { get; init; } = "";
    public bool Collapsible { get; init; } = true;
    public int MaxVisibleLines { get; init; } = 20;
    public bool ShowLineNumbers { get; init; } = true;
}

public sealed class CodeBlockComponent : Component
{
    public CodeBlockComponent(ComponentProps props) : base(props) { }

    public override int ComputeHeight(int availableWidth)
    {
        var codeProps = (CodeBlockProps)Props;
        var lines = codeProps.Code.Split('\n');
        var visibleLines = codeProps.Collapsible && lines.Length > codeProps.MaxVisibleLines
            ? codeProps.MaxVisibleLines + 2  // +2 for collapse indicators
            : lines.Length;
        return visibleLines + 2; // +2 for top/bottom border
    }

    public override void Render(TerminalBuffer buffer)
    {
        var codeProps = (CodeBlockProps)Props;
        var lines = codeProps.Code.Split('\n');
        var borderStyle = new TextStyle { Dim = true };
        var lineNumberStyle = new TextStyle { Dim = true };
        var codeStyle = new TextStyle { Foreground = Color.Default };

        var lineNumWidth = lines.Length.ToString().Length + 1;
        var x = LayoutRect.X;
        var y = LayoutRect.Y;
        var width = LayoutRect.Width;

        // Top border
        var topBorder = $"╭─ {codeProps.Language} " + new string('─', Math.Max(0, width - codeProps.Language.Length - 5));
        if (topBorder.Length > width)
            topBorder = topBorder[..width];
        buffer.SetText(x, y, topBorder, borderStyle);
        y++;

        // Determine visible lines
        var visibleLines = lines;
        var isCollapsed = false;
        if (codeProps.Collapsible && lines.Length > codeProps.MaxVisibleLines)
        {
            isCollapsed = true;
            visibleLines = lines[..codeProps.MaxVisibleLines];
        }

        // Render lines
        for (var i = 0; i < visibleLines.Length && y < buffer.Height - 1; i++)
        {
            var line = visibleLines[i];

            // Line number
            if (codeProps.ShowLineNumbers)
            {
                var lineNum = (i + 1).ToString().PadLeft(lineNumWidth);
                buffer.SetText(x, y, $"{lineNum} │ ", lineNumberStyle);

                // Syntax-highlighted code
                var highlighted = SyntaxHighlight(line, codeProps.Language);
                buffer.SetText(x + lineNumWidth + 3, y, highlighted, codeStyle);
            }
            else
            {
                var highlighted = SyntaxHighlight(line, codeProps.Language);
                buffer.SetText(x + 1, y, highlighted, codeStyle);
            }

            y++;
        }

        // Collapse indicator
        if (isCollapsed && y < buffer.Height - 1)
        {
            var remaining = lines.Length - codeProps.MaxVisibleLines;
            var collapseText = codeProps.ShowLineNumbers
                ? $"{new string(' ', lineNumWidth)}   ... {remaining} more lines"
                : $"  ... {remaining} more lines";
            buffer.SetText(x, y, collapseText, new TextStyle { Dim = true, Italic = true });
            y++;
        }

        // Bottom border
        if (y < buffer.Height)
        {
            var bottomBorder = "╰" + new string('─', width - 1);
            buffer.SetText(x, y, bottomBorder, borderStyle);
        }
    }

    internal static string SyntaxHighlight(string line, string language)
    {
        if (string.IsNullOrEmpty(line)) return line;

        var result = new StringBuilder();

        var keywords = GetKeywords(language);
        var (stringDelimiters, commentDelimiters) = GetDelimiters(language);

        var i = 0;
        while (i < line.Length)
        {
            // Check for line comments
            if (TryMatchComment(line, ref i, commentDelimiters, result))
                continue;

            // Check for strings
            if (TryMatchString(line, ref i, stringDelimiters, result))
                continue;

            // Check for numbers
            if (char.IsDigit(line[i]) && (i == 0 || !char.IsLetterOrDigit(line[i - 1])))
            {
                var start = i;
                while (i < line.Length && (char.IsDigit(line[i]) || line[i] is '.' or 'x' or 'b'))
                    i++;

                result.Append(new TextStyle { Foreground = Color.BrightYellow }.ToAnsiOpen());
                result.Append(line[start..i]);
                result.Append(AnsiCodes.Reset);
                continue;
            }

            // Check for identifiers/keywords
            if (char.IsLetter(line[i]) || line[i] == '_')
            {
                var start = i;
                while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_'))
                    i++;

                var word = line[start..i];

                if (keywords.Contains(word))
                {
                    result.Append(new TextStyle { Foreground = Color.BrightMagenta, Bold = true }.ToAnsiOpen());
                    result.Append(word);
                    result.Append(AnsiCodes.Reset);
                }
                else if (word.Length > 0 && char.IsUpper(word[0]))
                {
                    result.Append(new TextStyle { Foreground = Color.BrightCyan }.ToAnsiOpen());
                    result.Append(word);
                    result.Append(AnsiCodes.Reset);
                }
                else
                {
                    result.Append(word);
                }
                continue;
            }

            result.Append(line[i]);
            i++;
        }

        return result.ToString();
    }

    private static bool TryMatchComment(string line, ref int i, string[] commentDelimiters, StringBuilder result)
    {
        foreach (var delim in commentDelimiters)
        {
            if (i + delim.Length <= line.Length && line[i..(i + delim.Length)] == delim)
            {
                result.Append(new TextStyle { Foreground = Color.BrightBlack, Italic = true }.ToAnsiOpen());
                result.Append(line[i..]);
                result.Append(AnsiCodes.Reset);
                i = line.Length;
                return true;
            }
        }
        return false;
    }

    private static bool TryMatchString(string line, ref int i, char[] stringDelimiters, StringBuilder result)
    {
        if (!stringDelimiters.Contains(line[i])) return false;

        var quote = line[i];
        var start = i;
        i++;

        while (i < line.Length && line[i] != quote)
        {
            if (line[i] == '\\' && i + 1 < line.Length) i++;
            i++;
        }
        if (i < line.Length) i++; // closing quote

        result.Append(new TextStyle { Foreground = Color.BrightGreen }.ToAnsiOpen());
        result.Append(line[start..i]);
        result.Append(AnsiCodes.Reset);
        return true;
    }

    private static HashSet<string> GetKeywords(string language) => language.ToLowerInvariant() switch
    {
        "csharp" or "c#" or "cs" => ["using", "namespace", "class", "struct", "interface", "enum",
            "public", "private", "protected", "internal", "static", "readonly", "const",
            "void", "int", "string", "bool", "double", "float", "long", "byte", "char",
            "var", "new", "return", "if", "else", "for", "foreach", "while", "do",
            "switch", "case", "break", "continue", "try", "catch", "finally",
            "throw", "async", "await", "null", "true", "false", "this", "base",
            "override", "abstract", "virtual", "sealed", "in", "out", "ref",
            "get", "set", "init", "record", "with"],
        "python" or "py" => ["def", "class", "import", "from", "as", "return", "if", "elif",
            "else", "for", "while", "try", "except", "finally", "with", "async", "await",
            "lambda", "yield", "pass", "break", "continue", "raise", "and", "or", "not",
            "in", "is", "None", "True", "False", "self", "global", "nonlocal"],
        "javascript" or "js" or "typescript" or "ts" => ["function", "class", "const", "let", "var",
            "return", "if", "else", "for", "while", "do", "switch", "case", "break",
            "try", "catch", "finally", "throw", "async", "await", "new", "this",
            "import", "export", "from", "default", "extends", "super", "yield",
            "null", "undefined", "true", "false", "typeof", "instanceof",
            "interface", "type", "enum", "abstract", "implements"],
        "bash" or "sh" or "shell" or "zsh" => ["if", "then", "else", "elif", "fi", "for", "while",
            "do", "done", "case", "esac", "function", "return", "in", "select",
            "until", "echo", "exit", "local", "export", "readonly", "declare",
            "source", "alias", "set", "unset", "shift", "read", "printf"],
        "json" => ["true", "false", "null"],
        "rust" or "rs" => ["fn", "let", "mut", "const", "static", "struct", "enum", "impl",
            "trait", "pub", "use", "mod", "crate", "self", "super", "match",
            "if", "else", "for", "while", "loop", "return", "break", "continue",
            "async", "await", "move", "ref", "type", "where", "as", "in",
            "true", "false", "Some", "None", "Ok", "Err"],
        "go" => ["func", "var", "const", "type", "struct", "interface", "map", "chan",
            "package", "import", "return", "if", "else", "for", "range",
            "switch", "case", "default", "break", "continue", "go", "select",
            "defer", "fallthrough", "nil", "true", "false", "make", "new"],
        _ => []
    };

    private static (char[] stringDelimiters, string[] commentDelimiters) GetDelimiters(string language)
    {
        return language.ToLowerInvariant() switch
        {
            "python" or "py" => (['"', '\''], ["#"]),
            "bash" or "sh" or "shell" or "zsh" => (['"', '\''], ["#"]),
            "rust" or "rs" => (['"'], ["//"]),
            "go" => (['"', '`'], ["//"]),
            "json" => (['"'], []),
            _ => (['"', '\''], ["//", "#"])
        };
    }
}
