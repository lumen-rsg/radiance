using System.Text;
using Radiance.Builtins;
using Radiance.Interpreter;
using Radiance.Lexer;
using Radiance.Parser;
using Radiance.Utils;

namespace Radiance.Shell;

/// <summary>
/// The main interactive shell (REPL loop). Reads input, tokenizes with the lexer,
/// parses into an AST, and interprets via the AST-walking interpreter.
/// Supports multi-line input for control flow constructs (if/for/while/case/function).
/// Handles alias expansion, background jobs, tab completion, script execution,
/// and persistent history.
/// </summary>
public sealed class RadianceShell
{
    private const string Version = "0.7.0";

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
        "if", "for", "while", "until", "case", "function"
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

    /// <summary>
    /// Path to the user configuration file sourced on startup.
    /// </summary>
    private static readonly string ConfigFilePath = Path.Combine(
        Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".radiance_rc");

    public RadianceShell()
    {
        _context = InitializeContext();
        _builtins = BuiltinRegistry.CreateDefault();
        _processManager = new ProcessManager();
        _interpreter = new ShellInterpreter(_context, _builtins, _processManager);
        _history = new History();

        // Wire up history command with the history instance
        if (_builtins.TryGetCommand("history") is HistoryCommand historyCmd)
        {
            historyCmd.HistoryInstance = _history;
        }

        // Wire up exit handling
        ExitCommand.ExitRequested += (_, code) =>
        {
            _running = false;
            _context.LastExitCode = code;
        };

        // Wire up the script executor callback for the source builtin
        _context.ScriptExecutor = ExecuteInContext;
    }

    /// <summary>
    /// Starts the interactive REPL loop.
    /// Loads history and sources config before starting.
    /// </summary>
    /// <returns>The final exit code.</returns>
    public int Run()
    {
        // Load persistent history
        _history.Load();

        // Source user config if it exists
        SourceConfig();

        PrintWelcome();

        while (_running)
        {
            // Check for completed background jobs
            NotifyCompletedJobs();

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

        // Save persistent history on exit
        _history.Save();

        return _context.LastExitCode;
    }

    /// <summary>
    /// Executes a script file in a new shell context.
    /// Sets $0 to the script path and $1..$n to the provided arguments.
    /// </summary>
    /// <param name="scriptPath">The path to the script file.</param>
    /// <param name="args">The arguments (args[0] = $0, args[1..] = $1..$n).</param>
    /// <returns>The exit code of the script.</returns>
    public int ExecuteScript(string scriptPath, string[] args)
    {
        try
        {
            var content = File.ReadAllText(scriptPath);

            // Skip shebang line if present
            if (content.StartsWith("#!"))
            {
                var newlineIdx = content.IndexOf('\n');
                if (newlineIdx >= 0)
                    content = content[(newlineIdx + 1)..];
                else
                    content = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(content))
                return 0;

            // Set up script context
            _context.ShellName = scriptPath;

            // Set positional parameters
            var positionalParams = new List<string>();
            for (var i = 1; i < args.Length; i++)
            {
                positionalParams.Add(args[i]);
            }
            _context.SetPositionalParams(positionalParams);

            // Set script-related variables
            _context.SetVariable("BASH_SOURCE", scriptPath);

            return ExecuteInContext(content, args);
        }
        catch (Exception ex)
        {
            ColorOutput.WriteError($"{scriptPath}: {ex.Message}");
            return 126;
        }
    }

    /// <summary>
    /// Executes an inline command string (non-interactive mode, e.g., <c>radiance -c "command"</c>).
    /// </summary>
    /// <param name="command">The command string to execute.</param>
    /// <param name="args">Optional positional parameters (args[0] = $0, args[1..] = $1..$n).</param>
    /// <returns>The exit code of the command.</returns>
    public int ExecuteString(string command, string[]? args = null)
    {
        args ??= ["radiance"];

        _context.ShellName = args[0];

        if (args.Length > 1)
        {
            var positionalParams = new List<string>();
            for (var i = 1; i < args.Length; i++)
            {
                positionalParams.Add(args[i]);
            }
            _context.SetPositionalParams(positionalParams);
        }

        return ExecuteInContext(command, args);
    }

    /// <summary>
    /// Executes a script string in the current shell context.
    /// Used by the <c>source</c> builtin and script execution.
    /// Preserves and restores positional parameters for sourcing.
    /// </summary>
    /// <param name="content">The script content to execute.</param>
    /// <param name="args">The arguments (for positional parameter setup during sourcing).</param>
    /// <returns>The exit code.</returns>
    private int ExecuteInContext(string content, string[] args)
    {
        // Save and set positional parameters if args provided
        var hadPositionalParams = args.Length > 1;
        List<string>? savedParams = null;
        if (hadPositionalParams)
        {
            savedParams = _context.PositionalParams.ToList();
            var newParams = new List<string>();
            for (var i = 1; i < args.Length; i++)
            {
                newParams.Add(args[i]);
            }
            _context.SetPositionalParams(newParams);
        }

        try
        {
            // Expand aliases in the input
            var expandedInput = ExpandAliases(content);

            // Lex: input → tokens
            var lexer = new Lexer.Lexer(expandedInput);
            var tokens = lexer.Tokenize();

            // Parse: tokens → AST
            var parser = new Radiance.Parser.Parser(tokens);
            var ast = parser.Parse();

            if (ast is null)
                return 0;

            // Interpret: AST → execution
            return _interpreter.Execute(ast);
        }
        catch (Exception ex)
        {
            ColorOutput.WriteError(ex.Message);
            return 1;
        }
        finally
        {
            // Restore positional parameters if we changed them
            if (hadPositionalParams && savedParams is not null)
            {
                _context.SetPositionalParams(savedParams);
            }
        }
    }

    /// <summary>
    /// Sources the user configuration file if it exists.
    /// </summary>
    private void SourceConfig()
    {
        if (!File.Exists(ConfigFilePath))
            return;

        try
        {
            var content = File.ReadAllText(ConfigFilePath);
            if (string.IsNullOrWhiteSpace(content))
                return;

            // Execute silently — errors don't abort the shell
            try
            {
                var expandedInput = ExpandAliases(content);
                var lexer = new Lexer.Lexer(expandedInput);
                var tokens = lexer.Tokenize();
                var parser = new Radiance.Parser.Parser(tokens);
                var ast = parser.Parse();

                if (ast is not null)
                {
                    _interpreter.Execute(ast);
                }
            }
            catch (Exception ex)
            {
                ColorOutput.WriteWarning($"config: {ConfigFilePath}: {ex.Message}");
            }
        }
        catch
        {
            // Config file is optional — ignore errors
        }
    }

    /// <summary>
    /// Checks for completed background jobs and prints notifications.
    /// </summary>
    private void NotifyCompletedJobs()
    {
        var completed = _context.JobManager.UpdateAndCollectCompleted();
        foreach (var job in completed)
        {
            Console.WriteLine($"\n[{job.JobNumber}]+  Done                    {job.CommandText}");
        }
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

                // Handle { and } as block delimiters for functions
                if (token.Type == TokenType.LBrace)
                {
                    stack.Push("{");
                }
                else if (token.Type == TokenType.RBrace && stack.Count > 0 && stack.Peek() == "{")
                {
                    stack.Pop();
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
    /// Handles alias expansion before parsing.
    /// </summary>
    private void ExecuteInput(string input)
    {
        try
        {
            // Expand aliases in the input
            var expandedInput = ExpandAliases(input);

            // Lex: input → tokens
            var lexer = new Lexer.Lexer(expandedInput);
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
            ColorOutput.WriteError(ex.Message);
            _context.LastExitCode = 1;
        }
    }

    /// <summary>
    /// Performs alias expansion on the first word of each command in the input.
    /// Only expands aliases that are the first word in a command position.
    /// Prevents recursive expansion of an alias to itself.
    /// </summary>
    /// <param name="input">The raw input string.</param>
    /// <returns>The input with aliases expanded.</returns>
    private string ExpandAliases(string input)
    {
        // Simple approach: tokenize, check first word of each command for aliases
        var lines = input.Split('\n');

        for (var lineIdx = 0; lineIdx < lines.Length; lineIdx++)
        {
            var line = lines[lineIdx];
            var trimmed = line.TrimStart();

            if (string.IsNullOrEmpty(trimmed))
                continue;

            // Find the first word
            var firstWordEnd = 0;
            while (firstWordEnd < trimmed.Length && !char.IsWhiteSpace(trimmed[firstWordEnd])
                   && trimmed[firstWordEnd] != ';' && trimmed[firstWordEnd] != '|'
                   && trimmed[firstWordEnd] != '&' && trimmed[firstWordEnd] != '('
                   && trimmed[firstWordEnd] != '{')
            {
                firstWordEnd++;
            }

            if (firstWordEnd == 0)
                continue;

            var firstWord = trimmed[..firstWordEnd];

            // Don't expand aliases that are the same as the alias being defined
            if (firstWord == "alias" || firstWord == "unalias")
                continue;

            var expansion = _context.GetAlias(firstWord);
            if (expansion is not null)
            {
                // Replace the first word with the expansion
                var rest = trimmed[firstWordEnd..];
                lines[lineIdx] = expansion + rest;
            }
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Reads a line of input with basic line editing support.
    /// Supports history navigation (up/down), Ctrl+C, Ctrl+D, and tab completion.
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
                HandleTabCompletion(sb, left);
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
    /// Handles tab completion for the current input.
    /// </summary>
    /// <param name="sb">The current input buffer.</param>
    /// <param name="left">The starting cursor column.</param>
    private void HandleTabCompletion(StringBuilder sb, int left)
    {
        var input = sb.ToString();
        if (string.IsNullOrEmpty(input))
            return;

        // Find the last word being typed
        var lastSpace = input.LastIndexOf(' ');
        var prefix = lastSpace >= 0 ? input[(lastSpace + 1)..] : input;
        var hasSpace = lastSpace >= 0;

        if (!hasSpace)
        {
            // Command completion — check builtins, functions, aliases, then executables
            var matches = new List<string>();

            // Builtins
            foreach (var name in _builtins.CommandNames)
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                    matches.Add(name);
            }

            // Functions
            foreach (var name in _context.FunctionNames)
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal) && !matches.Contains(name))
                    matches.Add(name);
            }

            // Aliases
            foreach (var name in _context.Aliases.Keys)
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal) && !matches.Contains(name))
                    matches.Add(name);
            }

            // Executables in PATH
            try
            {
                var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
                foreach (var dir in pathDirs)
                {
                    try
                    {
                        if (!Directory.Exists(dir))
                            continue;
                        foreach (var file in Directory.EnumerateFiles(dir, $"{prefix}*"))
                        {
                            var name = Path.GetFileName(file);
                            if (!matches.Contains(name))
                                matches.Add(name);
                        }
                    }
                    catch { /* ignore permission errors */ }
                }
            }
            catch { /* ignore */ }

            ApplyCompletion(matches, sb, left, prefix);
        }
        else
        {
            // File/directory completion
            var matches = new List<string>();

            try
            {
                var dir = Path.GetDirectoryName(prefix);
                var filePrefix = Path.GetFileName(prefix);

                if (string.IsNullOrEmpty(dir))
                    dir = _context.CurrentDirectory;

                if (Directory.Exists(dir))
                {
                    foreach (var entry in Directory.EnumerateFileSystemEntries(dir, $"{filePrefix}*"))
                    {
                        var name = Path.GetFileName(entry);
                        if (Directory.Exists(entry))
                            name += Path.DirectorySeparatorChar;
                        matches.Add(name);
                    }
                }
            }
            catch { /* ignore */ }

            ApplyCompletion(matches, sb, left, prefix, lastSpace + 1);
        }
    }

    /// <summary>
    /// Applies tab completion results to the input buffer.
    /// </summary>
    /// <param name="matches">The completion candidates.</param>
    /// <param name="sb">The input buffer.</param>
    /// <param name="left">The starting cursor column.</param>
    /// <param name="prefix">The text being completed.</param>
    /// <param name="replaceStart">The index in the buffer to start replacing from (-1 for end of word before prefix).</param>
    private void ApplyCompletion(List<string> matches, StringBuilder sb, int left, string prefix, int replaceStart = -1)
    {
        if (matches.Count == 0)
            return;

        if (matches.Count == 1)
        {
            // Single match — complete it
            var completion = matches[0];
            var startPos = replaceStart >= 0 ? replaceStart : sb.Length - prefix.Length;

            ClearCurrentLine(left, sb.Length);
            sb.Remove(startPos, sb.Length - startPos);
            sb.Append(completion);
            Console.SetCursorPosition(left, Console.CursorTop);
            Console.Write(sb.ToString());
        }
        else
        {
            // Multiple matches — find common prefix and complete that
            var commonPrefix = FindCommonPrefix(matches);
            if (commonPrefix.Length > prefix.Length)
            {
                var startPos = replaceStart >= 0 ? replaceStart : sb.Length - prefix.Length;
                var toAdd = commonPrefix[prefix.Length..];

                ClearCurrentLine(left, sb.Length);
                sb.Append(toAdd);
                Console.SetCursorPosition(left, Console.CursorTop);
                Console.Write(sb.ToString());
            }
            else
            {
                // Show all matches
                Console.WriteLine();
                var maxNameLen = matches.Max(m => m.Length) + 2;
                var cols = Math.Max(1, Console.WindowWidth / maxNameLen);
                var i = 0;
                foreach (var match in matches)
                {
                    Console.Write(match.PadRight(maxNameLen));
                    i++;
                    if (i % cols == 0)
                        Console.WriteLine();
                }
                if (i % cols != 0)
                    Console.WriteLine();

                // Re-display prompt and current input
                var prompt = Prompt.Render(_context);
                Console.Write(prompt);
                Console.Write(sb.ToString());
            }
        }
    }

    /// <summary>
    /// Finds the longest common prefix among a set of strings.
    /// </summary>
    private static string FindCommonPrefix(List<string> strings)
    {
        if (strings.Count == 0)
            return string.Empty;

        var prefix = strings[0];
        foreach (var s in strings)
        {
            var minLen = Math.Min(prefix.Length, s.Length);
            var i = 0;
            while (i < minLen && prefix[i] == s[i])
                i++;
            prefix = prefix[..i];
        }

        return prefix;
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
        context.SetVariable("RADIANCE_VERSION", Version);

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
        Console.WriteLine();
        Console.WriteLine("\x1b[1;36m  ╭─────────────────────────────────╮\x1b[0m");
        Console.WriteLine($"\x1b[1;36m  │  \x1b[1;33m✦ Radiance Shell v{Version} ✦\x1b[1;36m      │\x1b[0m");
        Console.WriteLine("\x1b[1;36m  │  \x1b[37mA BASH interpreter in C#\x1b[1;36m       │\x1b[0m");
        Console.WriteLine("\x1b[1;36m  ╰─────────────────────────────────╯\x1b[0m");
        Console.WriteLine("\x1b[37m  Type 'exit' to quit, 'type' to inspect commands.\x1b[0m");
        Console.WriteLine();
    }
}