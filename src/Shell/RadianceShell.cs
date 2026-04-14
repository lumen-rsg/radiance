using System.Text;
using Radiance.Builtins;
using Radiance.Interpreter;
using Radiance.Lexer;
using Radiance.Parser;

namespace Radiance.Shell;

/// <summary>
/// The main interactive shell (REPL loop). Reads input, tokenizes with the lexer,
/// parses into an AST, and interprets via the AST-walking interpreter.
/// </summary>
public sealed class RadianceShell
{
    private readonly ShellContext _context;
    private readonly BuiltinRegistry _builtins;
    private readonly ProcessManager _processManager;
    private readonly ShellInterpreter _interpreter;
    private readonly History _history;
    private bool _running = true;

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

            // Read input
            var input = ReadLine();

            if (input is null)
            {
                // Ctrl+D — EOF
                Console.WriteLine();
                break;
            }

            if (string.IsNullOrWhiteSpace(input))
                continue;

            _history.Add(input);

            // Execute the input through the Lexer → Parser → Interpreter pipeline
            ExecuteInput(input);
        }

        return _context.LastExitCode;
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
        context.SetVariable("RADIANCE_VERSION", "0.3.0");

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
        var version = "0.3.0";
        Console.WriteLine();
        Console.WriteLine($"\x1b[1;36m  ╭─────────────────────────────────╮\x1b[0m");
        Console.WriteLine($"\x1b[1;36m  │  \x1b[1;33m✦ Radiance Shell v{version} ✦\x1b[1;36m      │\x1b[0m");
        Console.WriteLine($"\x1b[1;36m  │  \x1b[37mA BASH interpreter in C#\x1b[1;36m       │\x1b[0m");
        Console.WriteLine($"\x1b[1;36m  ╰─────────────────────────────────╯\x1b[0m");
        Console.WriteLine($"\x1b[37m  Type 'exit' to quit, 'type' to inspect commands.\x1b[0m");
        Console.WriteLine();
    }
}
