using System.Text;
using Radiance.Builtins;
using Radiance.Interpreter;
using Radiance.Lexer;
using Radiance.Parser;

namespace Radiance.Shell;

/// <summary>
/// The main interactive shell (REPL loop). Reads input, tokenizes with the lexer,
/// parses into an AST, and interprets via the AST-walking interpreter.
/// Supports multi-line input for control flow constructs (if/for/while/case).
/// </summary>
public sealed class RadianceShell
{
    private readonly ShellContext _context;
    private readonly BuiltinRegistry _builtins;
    private readonly ProcessManager _processManager;
    private readonly ShellInterpreter _interpreter;
    private readonly History _history;
    private bool _running = true;

    /// <summary>
    /// Block-opening keywords that require a matching closer.
    /// </summary>
    private static readonly HashSet<string> BlockOpeners = new(StringComparer.Ordinal)
    {
        "if", "for", "while", "until", "case"
    };

    /// <summary>
    /// Maps each block opener to its corresponding closer keyword.
    /// </summary>
    private static readonly Dictionary<string, string> BlockClosers = new(StringComparer.Ordinal)
    {
        ["if"] = "fi",
        ["for"] = "done",
        ["while"] = "done",
        ["until"] = "done",
        ["case"] = "esac"
    };

    public RadianceShell()
    {
        _context = InitializeContext();
        _builtins = BuiltinRegistry.CreateDefault();
        _processManager = new ProcessManager();
        _interpreter = new ShellInterpreter(_context, _builtins, _processManager);
        _history = new History();

        // Wire up exit handling
        ExitCommand.ExitRequested += (_, code) =>
        {
            _running = false;
            _context.LastExitCode = code;
        };
    }

    /// <summary>
    /// Starts the interactive REPL loop.
    /// </summary>
    /// <returns>The final exit code.</returns>
    public int Run()
    {
        PrintWelcome();

        while (_running)
        {
            // Render prompt
            var prompt = Prompt.Render(_context);
            Console.Write(prompt);

            // Read input (possibly multi-line)
            var input = ReadMultiLineInput(prompt);

            if (input is null)
            {
                // Ctrl+D — EOF
                Console.WriteLine();
                break;
            }

            if (string.IsNullOrWhiteSpace(input))
                continue;

            _history.Add(input.TrimEnd('\n'));

            // Execute the input through the Lexer → Parser → Interpreter pipeline
            ExecuteInput(input);
        }

        return _context.LastExitCode;
    }

    /// <summary>
    /// Reads input, continuing with a PS2 prompt ("> ") if block constructs are unclosed.
    /// </summary>
    /// <param name="ps1">The primary prompt string (for calculating left offset).</param>
    /// <returns>The full input string, possibly spanning multiple lines. Null on EOF.</returns>
    private string? ReadMultiLineInput(string ps1)
    {
        var sb = new StringBuilder();
        var firstLine = ReadLine();

        if (firstLine is null)
            return null;

        sb.Append(firstLine);

        // Check if we need more input (unclosed blocks)
        var blockStack = ComputeBlockStack(firstLine);

        while (blockStack.Count > 0)
        {
            // PS2 prompt — continuation
            Console.Write("> ");
            var continuation = ReadLine();

            if (continuation is null)
            {
                // EOF during continuation
                Console.WriteLine();
                break;
            }

            sb.Append('\n');
            sb.Append(continuation);

            // Update block stack with the new line
            var newStack = ComputeBlockStack(continuation, blockStack);
            blockStack = newStack;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Computes the block stack depth after parsing a line for keywords.
    /// Uses the lexer to tokenize and counts block openers/closers.
    /// </summary>
    /// <param name="line">The line to analyze.</param>
    /// <param name="existingStack">Optional existing block stack to build upon.</param>
    /// <returns>The updated block stack (empty if all blocks are closed).</returns>
    private Stack<string> ComputeBlockStack(string line, Stack<string>? existingStack = null)
    {
        var stack = existingStack ?? new Stack<string>();

        try
        {
            var lexer = new Lexer.Lexer(line);
            var tokens = lexer.Tokenize();

            var i = 0;
            while (i < tokens.Count)
            {
                var token = tokens[i];

                if (token.Type == TokenType.Eof)
                    break;

                // Only consider keywords in command position (first word in a pipeline,
                // or after separators like ;, |, &&, ||, newline)
                if (token.Type == TokenType.Word && IsInCommandPosition(tokens, i))
                {
                    if (BlockOpeners.Contains(token.Value))
                    {
                        stack.Push(token.Value);
                    }
                    else if (stack.Count > 0)
                    {
                        var currentBlock = stack.Peek();
                        if (BlockClosers.TryGetValue(currentBlock, out var closer) && token.Value == closer)
                        {
                            stack.Pop();
                        }
                    }
                }

                i++;
            }
        }
        catch
        {
            // If lexing fails (e.g., unterminated quote), don't change the stack
        }

        return stack;
    }

    /// <summary>
    /// Determines if a token at the given index is in "command position" — i.e.,
    /// it could be a keyword that starts or ends a block.
    /// A token is in command position if it's the first non-separator token,
    /// or follows a separator (;, newline, |, &&, ||).
    /// </summary>
    private static bool IsInCommandPosition(List<Token> tokens, int index)
    {
        // Look backwards for the previous meaningful token
        var prev = index - 1;
        while (prev >= 0 && tokens[prev].Type is TokenType.Comment)
            prev--;

        if (prev < 0)
            return true; // First token — command position

        var prevType = tokens[prev].Type;
        return prevType is TokenType.Semicolon or TokenType.Newline
            or TokenType.Pipe or TokenType.And or TokenType.Or
            or TokenType.DoubleSemicolon
            // After opening keywords (if, then, elif, else, do)
            or TokenType.LParen;
    }

    /// <summary>
    /// Executes a single line of input by lexing, parsing, and interpreting.
    /// </summary>
    private void ExecuteInput(string input)
    {
        try
        {
            // Lex: input → tokens
            var lexer = new Lexer.Lexer(input);
            var tokens = lexer.Tokenize();

            // Parse: tokens → AST
            var parser = new Radiance.Parser.Parser(tokens);
            var ast = parser.Parse();

            if (ast is null)
                return;

            // Interpret: AST → execution
            _context.LastExitCode = _interpreter.Execute(ast);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"radiance: error: {ex.Message}");
            _context.LastExitCode = 1;
        }
    }

    /// <summary>
    /// Reads a line of input with basic line editing support.
    /// </summary>
    private string? ReadLine()
    {
        var sb = new StringBuilder();
        var left = Console.CursorLeft;

        while (true)
        {
            var key = Console.ReadKey(true);

            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return sb.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0)
                {
                    sb.Remove(sb.Length - 1, 1);
                    Console.Write("\b \b");
                }

                continue;
            }

            if (key.Key == ConsoleKey.UpArrow)
            {
                var entry = _history.NavigateUp();
                if (entry is not null)
                {
                    ClearCurrentLine(left, sb.Length);
                    sb.Clear();
                    sb.Append(entry);
                    Console.SetCursorPosition(left, Console.CursorTop);
                    Console.Write(entry);
                }

                continue;
            }

            if (key.Key == ConsoleKey.DownArrow)
            {
                var entry = _history.NavigateDown();
                ClearCurrentLine(left, sb.Length);
                sb.Clear();
                if (entry is not null)
                {
                    sb.Append(entry);
                }

                Console.SetCursorPosition(left, Console.CursorTop);
                Console.Write(sb.ToString());
                continue;
            }

            if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                Console.WriteLine("^C");
                return string.Empty;
            }

            if (key.Key == ConsoleKey.D && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (sb.Length == 0)
                    return null;

                // If there's text, treat Ctrl+D as delete char (like bash)
                continue;
            }

            if (key.Key == ConsoleKey.Tab)
            {
                // Tab completion — future phase
                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                sb.Append(key.KeyChar);
                Console.Write(key.KeyChar);
            }
        }
    }

    /// <summary>
    /// Clears the current line content on screen.
    /// </summary>
    private static void ClearCurrentLine(int startLeft, int length)
    {
        Console.SetCursorPosition(startLeft, Console.CursorTop);
        Console.Write(new string(' ', length));
        Console.SetCursorPosition(startLeft, Console.CursorTop);
    }

    /// <summary>
    /// Initializes the execution context with default environment variables.
    /// </summary>
    private static ShellContext InitializeContext()
    {
        var context = new ShellContext();

        // Set shell variables
        context.SetVariable("PWD", context.CurrentDirectory);
        context.SetVariable("SHELL", "radiance");
        context.SetVariable("RADIANCE_VERSION", "0.5.0");

        // Export key variables
        context.ExportVariable("PATH");
        context.ExportVariable("HOME");
        context.ExportVariable("USER");
        context.ExportVariable("SHELL");
        context.ExportVariable("PWD");

        return context;
    }

    /// <summary>
    /// Prints the welcome banner.
    /// </summary>
    private static void PrintWelcome()
    {
        var version = "0.5.0";
        Console.WriteLine();
        Console.WriteLine($"\x1b[1;36m  ╭─────────────────────────────────╮\x1b[0m");
        Console.WriteLine($"\x1b[1;36m  │  \x1b[1;33m✦ Radiance Shell v{version} ✦\x1b[1;36m      │\x1b[0m");
        Console.WriteLine($"\x1b[1;36m  │  \x1b[37mA BASH interpreter in C#\x1b[1;36m       │\x1b[0m");
        Console.WriteLine($"\x1b[1;36m  ╰─────────────────────────────────╯\x1b[0m");
        Console.WriteLine($"\x1b[37m  Type 'exit' to quit, 'type' to inspect commands.\x1b[0m");
        Console.WriteLine();
    }
}