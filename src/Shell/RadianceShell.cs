using System.Diagnostics;
using System.Text;
using Radiance.Builtins;
using Radiance.Interpreter;
using Radiance.Lexer;
using Radiance.Parser;
using Radiance.Plugins;
using Radiance.Themes;
using Radiance.Utils;

namespace Radiance.Shell;

/// <summary>
/// The main interactive shell (REPL loop). Reads input, tokenizes with the lexer,
/// parses into an AST, and interprets via the AST-walking interpreter.
/// Supports multi-line input for control flow constructs (if/for/while/case/function).
/// Handles alias expansion, background jobs, tab completion, script execution,
/// persistent history, and full line editing.
/// </summary>
public sealed class RadianceShell
{
    private const string Version = "1.2.3";

    private readonly ShellContext _context;
    private readonly BuiltinRegistry _builtins;
    private readonly ProcessManager _processManager;
    private readonly ShellInterpreter _interpreter;
    private readonly History _history;
    private readonly PluginManager _pluginManager;
    private readonly ThemeManager _themeManager = new();
    private readonly SessionStats _sessionStats = new();
    private readonly bool _isLoginShell;
    private bool _running = true;

    /// <summary>
    /// Cached list of PATH executables for tab completion.
    /// Populated lazily and refreshed when needed.
    /// </summary>
    private List<string>? _pathExecutableCache;
    private DateTime _pathCacheTime = DateTime.MinValue;
    private static readonly TimeSpan PathCacheTimeout = TimeSpan.FromSeconds(5);

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

    /// <summary>
    /// Login shell profile files sourced in order (first found wins for user-level).
    /// </summary>
    private static readonly string HomeDir = Environment.GetEnvironmentVariable("HOME")
        ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static readonly string[] UserProfileFiles =
    [
        Path.Combine(HomeDir, ".bash_profile"),
        Path.Combine(HomeDir, ".bash_login"),
        Path.Combine(HomeDir, ".profile")
    ];

    private static readonly string SystemProfileFile = "/etc/profile";

    /// <summary>
    /// Creates a new Radiance shell instance.
    /// </summary>
    /// <param name="isLoginShell">If true, the shell runs in login mode and sources system/user profiles.</param>
    public RadianceShell(bool isLoginShell = false)
    {
        _isLoginShell = isLoginShell;
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

        // Wire up exec handling
        ExecCommand.ExecRequested += (_, code) =>
        {
            _running = false;
            _context.LastExitCode = code;
        };

        // Wire up the script executor callback for the source builtin
        _context.ScriptExecutor = ExecuteInContext;

        // Wire up script file executor callback for shebang script execution
        _context.ScriptFileExecutor = ExecuteScript;

        // Wire up command line executor callback for the exec builtin
        _context.CommandLineExecutor = ExecuteCommandLine;

        // Initialize theme system
        _themeManager.Initialize();

        // Initialize plugin system
        _pluginManager = new PluginManager(_context, _builtins);
        var pluginCmd = new PluginCommand { Manager = _pluginManager };
        _builtins.Register(pluginCmd);

        // Register theme command
        _builtins.Register(new ThemeCommand(_themeManager));

        // Wire up radiance command with session stats and version
        if (_builtins.TryGetCommand("radiance") is RadianceCommand radianceCmd)
        {
            radianceCmd.Stats = _sessionStats;
            radianceCmd.Version = Version;
        }
    }

    /// <summary>
    /// Starts the interactive REPL loop.
    /// Loads history and sources config before starting.
    /// When stdin is redirected (piped input), reads lines via Console.ReadLine
    /// instead of Console.ReadKey to avoid InvalidOperationException.
    /// </summary>
    /// <returns>The final exit code.</returns>
    public int Run()
    {
        // Load persistent history
        _history.Load();

        // Source login profiles if this is a login shell
        if (_isLoginShell)
        {
            SourceLoginProfiles();
        }

        // Source user config if it exists
        SourceConfig();

        // Load plugins from ~/.radiance/plugins/
        LoadPlugins();

        PrintWelcome();

        var isInputRedirected = Console.IsInputRedirected;

        while (_running)
        {
            // Check for completed background jobs
            NotifyCompletedJobs();

            // Render prompt using theme system
            var prompt = RenderThemePrompt();
            Console.Write(prompt);

            // Read input (possibly multi-line)
            string? input;
            if (isInputRedirected)
            {
                input = ReadLineRedirected();
            }
            else
            {
                input = ReadMultiLineInput(prompt);
            }

            if (input is null)
            {
                // Ctrl+D — EOF
                Console.WriteLine();
                break;
            }

            if (string.IsNullOrWhiteSpace(input))
                continue;

            _history.Add(input.TrimEnd('\n'));

            // Expand history macros (e.g., !! → last command)
            input = ExpandHistoryMacros(input);

            // Execute the input through the Lexer → Parser → Interpreter pipeline
            ExecuteInput(input);
        }

        // Save persistent history on exit
        _history.Save();

        // Unload all plugins on exit
        _pluginManager.UnloadAll();

        return _context.LastExitCode;
    }

    /// <summary>
    /// Reads a line from redirected stdin (pipe mode).
    /// Returns null on EOF. Handles multi-line block constructs.
    /// </summary>
    private string? ReadLineRedirected()
    {
        var sb = new StringBuilder();
        var firstLine = Console.ReadLine();

        if (firstLine is null)
            return null;

        sb.Append(firstLine);

        // Check if we need more input (unclosed blocks)
        var blockStack = ComputeBlockStack(firstLine);

        while (blockStack.Count > 0)
        {
            Console.Write("> ");
            var continuation = Console.ReadLine();

            if (continuation is null)
            {
                Console.WriteLine();
                break;
            }

            sb.Append('\n');
            sb.Append(continuation);

            var newStack = ComputeBlockStack(continuation, blockStack);
            blockStack = newStack;
        }

        return sb.ToString();
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
    /// Sources login shell profiles in BASH-compatible order.
    /// Order: /etc/profile, then first found of ~/.bash_profile, ~/.bash_login, ~/.profile.
    /// Only called when the shell is invoked as a login shell (-l or argv[0] starts with '-').
    /// </summary>
    private void SourceLoginProfiles()
    {
        // Set the SHELL environment variable to the Radiance executable path
        var exePath = Process.GetCurrentProcess().MainModule?.FileName
            ?? Environment.GetCommandLineArgs().FirstOrDefault()
            ?? "radiance";
        Environment.SetEnvironmentVariable("SHELL", exePath);
        _context.SetVariable("SHELL", exePath);

        // Source /etc/profile (system-wide)
        SourceFileIfExists(SystemProfileFile, "system profile");

        // Source first found user-level profile (BASH order: .bash_profile → .bash_login → .profile)
        foreach (var profileFile in UserProfileFiles)
        {
            if (File.Exists(profileFile))
            {
                SourceFileIfExists(profileFile, "user profile");
                break; // Only source the first one found (BASH behavior)
            }
        }
    }

    /// <summary>
    /// Sources a shell script file if it exists. Errors are reported but don't abort the shell.
    /// </summary>
    /// <param name="filePath">The path to the script file.</param>
    /// <param name="label">A human-readable label for error messages.</param>
    private void SourceFileIfExists(string filePath, string label)
    {
        if (!File.Exists(filePath))
            return;

        try
        {
            var content = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(content))
                return;

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
                return;

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
                ColorOutput.WriteWarning($"{label}: {filePath}: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            ColorOutput.WriteWarning($"{label}: {filePath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads plugins from the default plugin directory.
    /// </summary>
    private void LoadPlugins()
    {
        try
        {
            _pluginManager.LoadAll();

            if (_pluginManager.LoadedCount > 0)
            {
                ColorOutput.WriteInfo($"Loaded {_pluginManager.LoadedCount} plugin(s)");
            }
        }
        catch (Exception ex)
        {
            ColorOutput.WriteWarning($"plugins: {ex.Message}");
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
    /// Handles alias expansion before parsing. Tracks session statistics.
    /// </summary>
    private void ExecuteInput(string input)
    {
        // Track session statistics — extract the first word as the command name
        var trimmedInput = input.TrimStart();
        var cmdEnd = 0;
        while (cmdEnd < trimmedInput.Length && !char.IsWhiteSpace(trimmedInput[cmdEnd])
               && trimmedInput[cmdEnd] != ';' && trimmedInput[cmdEnd] != '|'
               && trimmedInput[cmdEnd] != '&')
        {
            cmdEnd++;
        }

        if (cmdEnd > 0)
        {
            _sessionStats.RecordCommand(trimmedInput[..cmdEnd]);
        }

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
    /// Executes a raw command line string (used by the <c>exec</c> builtin).
    /// Runs through the full lex → parse → interpret pipeline with alias expansion.
    /// </summary>
    /// <param name="commandLine">The command line to execute.</param>
    /// <returns>The exit code.</returns>
    private int ExecuteCommandLine(string commandLine)
    {
        try
        {
            var expandedInput = ExpandAliases(commandLine);
            var lexer = new Lexer.Lexer(expandedInput);
            var tokens = lexer.Tokenize();
            var parser = new Radiance.Parser.Parser(tokens);
            var ast = parser.Parse();

            if (ast is null)
                return 0;

            return _interpreter.Execute(ast);
        }
        catch (Exception ex)
        {
            ColorOutput.WriteError(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// Expands history macros in the input string.
    /// Supports:
    /// <list type="bullet">
    /// <item><c>!!</c> — replaced with the last command from history</item>
    /// </list>
    /// Prints the expanded command so the user can see what is being executed.
    /// Skips expansion inside single-quoted strings.
    /// </summary>
    /// <param name="input">The raw input string.</param>
    /// <returns>The input with history macros expanded, or the original input if no expansion occurred.</returns>
    private string ExpandHistoryMacros(string input)
    {
        if (!_history.GetAll().Any())
            return input;

        var expanded = ExpandBangBang(input);
        return expanded;
    }

    /// <summary>
    /// Replaces all <c>!!</c> occurrences with the last history entry,
    /// respecting single-quoted strings (no expansion inside them).
    /// Prints the expanded command so the user sees what will execute.
    /// </summary>
    /// <param name="input">The input potentially containing <c>!!</c>.</param>
    /// <returns>The expanded input.</returns>
    private string ExpandBangBang(string input)
    {
        if (!input.Contains("!!"))
            return input;

        var lastCommand = _history.GetEntry(_history.Count);
        if (lastCommand is null)
        {
            ColorOutput.WriteError("!!: event not found (history is empty)");
            return input;
        }

        var sb = new StringBuilder();
        var i = 0;
        var inSingleQuote = false;

        while (i < input.Length)
        {
            // Track single-quote state
            if (input[i] == '\'' && (i == 0 || input[i - 1] != '\\'))
            {
                inSingleQuote = !inSingleQuote;
                sb.Append(input[i]);
                i++;
                continue;
            }

            // Check for !! outside single quotes
            if (!inSingleQuote && i + 1 < input.Length && input[i] == '!' && input[i + 1] == '!')
            {
                sb.Append(lastCommand);
                i += 2;
                continue;
            }

            sb.Append(input[i]);
            i++;
        }

        var result = sb.ToString();

        // Print the expanded command so the user sees what runs (BASH behavior)
        if (result != input)
        {
            Console.WriteLine(result);
        }

        return result;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Line Editing
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reads a line of input with full line editing support.
    /// Supports cursor movement, history navigation, Ctrl shortcuts,
    /// tab completion, and reverse history search (Ctrl+R).
    /// </summary>
    private string? ReadLine()
    {
        var sb = new StringBuilder();
        var cursorPos = 0;  // Position within the buffer (0 = before first char)
        var startLeft = Console.CursorLeft;

        while (true)
        {
            var key = Console.ReadKey(true);

            // ─── Enter ───
            if (key.Key == ConsoleKey.Enter)
            {
                // Move cursor to end of line and output newline
                SetCursorPosition(startLeft, sb, cursorPos, sb.Length);
                Console.WriteLine();
                return sb.ToString();
            }

            // ─── Backspace ───
            if (key.Key == ConsoleKey.Backspace)
            {
                if (cursorPos > 0)
                {
                    sb.Remove(cursorPos - 1, 1);
                    cursorPos--;
                    RedisplayLine(startLeft, sb, cursorPos);
                }
                continue;
            }

            // ─── Delete key ───
            if (key.Key == ConsoleKey.Delete)
            {
                if (cursorPos < sb.Length)
                {
                    sb.Remove(cursorPos, 1);
                    RedisplayLine(startLeft, sb, cursorPos);
                }
                continue;
            }

            // ─── Left Arrow ───
            if (key.Key == ConsoleKey.LeftArrow)
            {
                if (cursorPos > 0)
                {
                    cursorPos--;
                    SetCursorPosition(startLeft, sb, cursorPos);
                }
                continue;
            }

            // ─── Right Arrow ───
            if (key.Key == ConsoleKey.RightArrow)
            {
                if (cursorPos < sb.Length)
                {
                    cursorPos++;
                    SetCursorPosition(startLeft, sb, cursorPos);
                }
                continue;
            }

            // ─── Up Arrow — History backward ───
            if (key.Key == ConsoleKey.UpArrow)
            {
                var entry = _history.NavigateUp();
                if (entry is not null)
                {
                    sb.Clear();
                    sb.Append(entry);
                    cursorPos = sb.Length;
                    RedisplayLine(startLeft, sb, cursorPos);
                }
                continue;
            }

            // ─── Down Arrow — History forward ───
            if (key.Key == ConsoleKey.DownArrow)
            {
                var entry = _history.NavigateDown();
                sb.Clear();
                if (entry is not null)
                    sb.Append(entry);
                cursorPos = sb.Length;
                RedisplayLine(startLeft, sb, cursorPos);
                continue;
            }

            // ─── Home ───
            if (key.Key == ConsoleKey.Home)
            {
                cursorPos = 0;
                SetCursorPosition(startLeft, sb, cursorPos);
                continue;
            }

            // ─── End ───
            if (key.Key == ConsoleKey.End)
            {
                cursorPos = sb.Length;
                SetCursorPosition(startLeft, sb, cursorPos);
                continue;
            }

            // ─── Ctrl+C — Cancel line ───
            if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                // Move to end of line, print ^C and newline
                SetCursorPosition(startLeft, sb, cursorPos, sb.Length);
                Console.WriteLine("^C");
                return string.Empty;
            }

            // ─── Ctrl+D — EOF or delete ───
            if (key.Key == ConsoleKey.D && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (sb.Length == 0)
                    return null; // EOF

                // If there's text, delete char at cursor (like bash)
                if (cursorPos < sb.Length)
                {
                    sb.Remove(cursorPos, 1);
                    RedisplayLine(startLeft, sb, cursorPos);
                }
                continue;
            }

            // ─── Ctrl+A — Move to beginning of line ───
            if (key.Key == ConsoleKey.A && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                cursorPos = 0;
                SetCursorPosition(startLeft, sb, cursorPos);
                continue;
            }

            // ─── Ctrl+E — Move to end of line ───
            if (key.Key == ConsoleKey.E && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                cursorPos = sb.Length;
                SetCursorPosition(startLeft, sb, cursorPos);
                continue;
            }

            // ─── Ctrl+L — Clear screen ───
            if (key.Key == ConsoleKey.L && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                Console.Clear();
                var prompt = Prompt.Render(_context);
                Console.Write(prompt);
                startLeft = Console.CursorLeft;
                RedisplayLine(startLeft, sb, cursorPos);
                continue;
            }

            // ─── Ctrl+K — Kill from cursor to end of line ───
            if (key.Key == ConsoleKey.K && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (cursorPos < sb.Length)
                {
                    sb.Remove(cursorPos, sb.Length - cursorPos);
                    RedisplayLine(startLeft, sb, cursorPos);
                }
                continue;
            }

            // ─── Ctrl+U — Kill from beginning of line to cursor ───
            if (key.Key == ConsoleKey.U && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (cursorPos > 0)
                {
                    sb.Remove(0, cursorPos);
                    cursorPos = 0;
                    RedisplayLine(startLeft, sb, cursorPos);
                }
                continue;
            }

            // ─── Ctrl+W — Delete word backward ───
            if (key.Key == ConsoleKey.W && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (cursorPos > 0)
                {
                    var wordStart = cursorPos - 1;
                    // Skip trailing whitespace
                    while (wordStart > 0 && char.IsWhiteSpace(sb[wordStart]))
                        wordStart--;
                    // Skip the word
                    while (wordStart > 0 && !char.IsWhiteSpace(sb[wordStart - 1]))
                        wordStart--;

                    sb.Remove(wordStart, cursorPos - wordStart);
                    cursorPos = wordStart;
                    RedisplayLine(startLeft, sb, cursorPos);
                }
                continue;
            }

            // ─── Ctrl+R — Reverse history search ───
            if (key.Key == ConsoleKey.R && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                HandleReverseSearch(sb, startLeft);
                cursorPos = sb.Length;
                startLeft = RedisplayLineGetStartLeft(startLeft, sb, cursorPos);
                continue;
            }

            // ─── Escape — Clear line ───
            if (key.Key == ConsoleKey.Escape)
            {
                sb.Clear();
                cursorPos = 0;
                RedisplayLine(startLeft, sb, cursorPos);
                continue;
            }

            // ─── Tab — Completion ───
            if (key.Key == ConsoleKey.Tab)
            {
                HandleTabCompletion(sb, ref cursorPos, ref startLeft);
                continue;
            }

            // ─── Normal character ───
            if (!char.IsControl(key.KeyChar))
            {
                sb.Insert(cursorPos, key.KeyChar);
                cursorPos++;
                RedisplayLine(startLeft, sb, cursorPos);
                continue;
            }
        }
    }

    /// <summary>
    /// Redisplays the current input line on the terminal from the given start position.
    /// Clears any previous content and positions the cursor correctly.
    /// </summary>
    /// <param name="startLeft">The starting column position of the input.</param>
    /// <param name="sb">The input buffer.</param>
    /// <param name="cursorPos">The desired cursor position within the buffer.</param>
    private static void RedisplayLine(int startLeft, StringBuilder sb, int cursorPos)
    {
        var text = sb.ToString();

        // Move cursor to start position, write text, clear any remaining old content
        Console.SetCursorPosition(startLeft, Console.CursorTop);
        Console.Write(text);
        Console.Write("\x1b[K"); // ANSI: clear from cursor to end of line

        // Position cursor
        SetCursorPosition(startLeft, sb, cursorPos);
    }

    /// <summary>
    /// Redisplays the line and returns the new startLeft (used after Ctrl+R which re-renders prompt).
    /// </summary>
    private static int RedisplayLineGetStartLeft(int startLeft, StringBuilder sb, int cursorPos)
    {
        var text = sb.ToString();
        Console.SetCursorPosition(startLeft, Console.CursorTop);
        Console.Write(text);
        Console.Write("\x1b[K"); // ANSI: clear from cursor to end of line
        SetCursorPosition(startLeft, sb, cursorPos);
        return startLeft;
    }

    /// <summary>
    /// Sets the cursor position based on a character offset within the buffer.
    /// Handles line wrapping by computing the actual column and row.
    /// </summary>
    /// <param name="startLeft">The starting column where input begins.</param>
    /// <param name="sb">The input buffer (used for length calculation).</param>
    /// <param name="charOffset">The character offset within the buffer.</param>
    /// <param name="maxLength">Optional max length to use for calculations (defaults to sb.Length).</param>
    private static void SetCursorPosition(int startLeft, StringBuilder sb, int charOffset, int? maxLength = null)
    {
        var windowWidth = Console.WindowWidth > 0 ? Console.WindowWidth : 80;
        var effectiveLen = maxLength ?? sb.Length;
        var totalWidth = startLeft + effectiveLen;
        var cursorAbsolute = startLeft + charOffset;

        var startRow = Console.CursorTop - ((startLeft + effectiveLen) / windowWidth);

        // If the text wraps, compute the row offset
        if (totalWidth >= windowWidth)
        {
            // How many rows the text occupies
            var cursorRow = startRow + (cursorAbsolute / windowWidth);
            var cursorCol = cursorAbsolute % windowWidth;
            Console.SetCursorPosition(cursorCol, cursorRow);
        }
        else
        {
            Console.SetCursorPosition(cursorAbsolute, Console.CursorTop);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Reverse History Search (Ctrl+R)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Handles Ctrl+R reverse incremental history search.
    /// Shows a "(reverse-i-search)`query': match" prompt, updating in real-time
    /// as the user types. Ctrl+R cycles to older matches. Enter accepts, 
    /// Ctrl+C/Esc cancels.
    /// </summary>
    /// <param name="sb">The input buffer to populate with the selected match.</param>
    /// <param name="startLeft">The starting cursor column.</param>
    private void HandleReverseSearch(StringBuilder sb, int startLeft)
    {
        var query = new StringBuilder();
        var matches = new List<string>();
        var matchIndex = -1;

        while (true)
        {
            // Clear current line and show search prompt
            Console.SetCursorPosition(startLeft, Console.CursorTop);
            Console.Write(new string(' ', Math.Max(sb.Length + 20, 40)));
            Console.SetCursorPosition(startLeft, Console.CursorTop);

            var queryStr = query.ToString();
            var matchStr = matchIndex >= 0 && matchIndex < matches.Count ? matches[matchIndex] : "";
            Console.Write($"\x1b[1;33m(reverse-i-search)`{queryStr}`: \x1b[0m{matchStr}");

            var key = Console.ReadKey(true);

            if (key.Key == ConsoleKey.Enter)
            {
                // Accept match
                Console.SetCursorPosition(startLeft, Console.CursorTop);
                Console.Write(new string(' ', queryStr.Length + matchStr.Length + 30));
                Console.SetCursorPosition(startLeft, Console.CursorTop);

                sb.Clear();
                if (matchIndex >= 0 && matchIndex < matches.Count)
                    sb.Append(matches[matchIndex]);
                else
                    sb.Append(queryStr);
                return;
            }

            if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                // Cancel — restore original line
                Console.SetCursorPosition(startLeft, Console.CursorTop);
                Console.Write(new string(' ', queryStr.Length + matchStr.Length + 30));
                Console.SetCursorPosition(startLeft, Console.CursorTop);
                Console.Write(sb.ToString());
                return;
            }

            if (key.Key == ConsoleKey.Escape)
            {
                // Cancel — restore original line
                Console.SetCursorPosition(startLeft, Console.CursorTop);
                Console.Write(new string(' ', queryStr.Length + matchStr.Length + 30));
                Console.SetCursorPosition(startLeft, Console.CursorTop);
                Console.Write(sb.ToString());
                return;
            }

            if (key.Key == ConsoleKey.R && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                // Cycle to next older match
                if (matches.Count > 0 && matchIndex + 1 < matches.Count)
                {
                    matchIndex++;
                }
                continue;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (query.Length > 0)
                {
                    query.Remove(query.Length - 1, 1);
                    // Re-search with shorter query
                    matches = _history.SearchEntries(query.ToString()).ToList();
                    matchIndex = matches.Count > 0 ? 0 : -1;
                }
                continue;
            }

            // Normal character — add to query and search
            if (!char.IsControl(key.KeyChar))
            {
                query.Append(key.KeyChar);
                matches = _history.SearchEntries(query.ToString()).ToList();
                matchIndex = matches.Count > 0 ? 0 : -1;
                continue;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tab Completion
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Handles tab completion for the current input.
    /// Supports command completion, file/path completion with tilde and variable
    /// expansion, and directory-only completion for <c>cd</c>.
    /// </summary>
    /// <param name="sb">The current input buffer.</param>
    /// <param name="cursorPos">The current cursor position (may be updated).</param>
    /// <param name="left">The starting cursor column.</param>
    private void HandleTabCompletion(StringBuilder sb, ref int cursorPos, ref int startLeft)
    {
        var input = sb.ToString();
        if (string.IsNullOrEmpty(input))
            return;

        // Find the word being typed at the cursor position
        var wordStart = FindWordStart(input, cursorPos);
        var prefix = input[wordStart..cursorPos];
        var isFirstWord = IsFirstWord(input, wordStart);

        if (isFirstWord)
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

            // Executables in PATH (cached)
            foreach (var name in GetPathExecutables())
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal) && !matches.Contains(name))
                    matches.Add(name);
            }

            ApplyCompletion(matches, sb, ref cursorPos, ref startLeft, wordStart, prefix);
        }
        else
        {
            // File/directory completion
            var commandName = GetCommandName(input);
            var dirsOnly = commandName is "cd" or "pushd" or "popd" or "rmdir" or "mkdir";
            var matches = CompletePath(prefix, dirsOnly);
            ApplyCompletion(matches, sb, ref cursorPos, ref startLeft, wordStart, prefix);
        }
    }

    /// <summary>
    /// Completes a file/directory path with support for tilde expansion,
    /// environment variable expansion, and absolute paths.
    /// </summary>
    /// <param name="prefix">The path prefix to complete.</param>
    /// <param name="dirsOnly">If true, only return directories.</param>
    /// <returns>A list of matching completions.</returns>
    private List<string> CompletePath(string prefix, bool dirsOnly)
    {
        var matches = new List<string>();

        try
        {
            // Find the directory boundary in the original prefix.
            // Everything up to and including the last '/' is the directory part;
            // everything after is the filename prefix to match against.
            var lastSlash = prefix.LastIndexOf('/');
            string originalDirPart;
            string filePrefix;

            if (lastSlash >= 0)
            {
                originalDirPart = prefix[..(lastSlash + 1)]; // e.g. "/opt/", "~/", "src/"
                filePrefix = prefix[(lastSlash + 1)..];       // e.g. "", "an", "te"
            }
            else
            {
                originalDirPart = "";
                filePrefix = prefix;
            }

            // Expand the directory part for actual filesystem search
            var searchDir = string.IsNullOrEmpty(originalDirPart)
                ? _context.CurrentDirectory
                : ExpandPathPrefix(originalDirPart).TrimEnd('/', Path.DirectorySeparatorChar);

            if (!Directory.Exists(searchDir))
                return matches;

            foreach (var entry in Directory.EnumerateFileSystemEntries(searchDir, $"{filePrefix}*"))
            {
                var isDir = Directory.Exists(entry);
                if (dirsOnly && !isDir)
                    continue;

                var name = Path.GetFileName(entry);

                // Hide dot-files unless the file prefix starts with a dot (BASH behavior)
                if (name.StartsWith('.') && !filePrefix.StartsWith('.'))
                    continue;

                if (isDir)
                    name += '/';

                // Return the full completion: original dir part + filename
                // This preserves ~, $VAR, etc. in the user's typed prefix
                matches.Add(originalDirPart + name);
            }
        }
        catch { /* ignore permission errors */ }

        return matches;
    }

    /// <summary>
    /// Expands ~ and $VAR at the beginning of a path prefix for completion searching.
    /// Returns the expanded absolute path, but the completion result preserves
    /// the original prefix form.
    /// </summary>
    /// <param name="prefix">The raw path prefix (may contain ~ or $VAR).</param>
    /// <returns>The expanded absolute path.</returns>
    private string ExpandPathPrefix(string prefix)
    {
        // Handle tilde expansion
        if (prefix.StartsWith("~/"))
        {
            var home = Environment.GetEnvironmentVariable("HOME") ?? "";
            return home + prefix[1..];
        }

        if (prefix == "~")
        {
            return Environment.GetEnvironmentVariable("HOME") ?? "";
        }

        // Handle ~user expansion
        if (prefix.Length > 1 && prefix[0] == '~' && prefix[1] != '/')
        {
            var slashIdx = prefix.IndexOf('/');
            var username = slashIdx >= 0 ? prefix[1..slashIdx] : prefix[1..];
            // On macOS/Linux, ~user expands to /Users/user or /home/user
            var userHome = Path.Combine("/Users", username);
            if (!Directory.Exists(userHome))
                userHome = Path.Combine("/home", username);
            if (Directory.Exists(userHome))
            {
                return slashIdx >= 0 ? userHome + prefix[slashIdx..] : userHome;
            }
        }

        // Handle $VAR/... expansion
        if (prefix.StartsWith('$'))
        {
            var slashIdx = prefix.IndexOf('/');
            var varName = slashIdx >= 0 ? prefix[1..slashIdx] : prefix[1..];
            var value = _context.GetVariable(varName);
            if (!string.IsNullOrEmpty(value))
            {
                return slashIdx >= 0 ? value + prefix[slashIdx..] : value;
            }
        }

        // Handle absolute paths
        if (prefix.StartsWith('/'))
            return prefix;

        // Relative path — resolve against CWD
        return Path.GetFullPath(Path.Combine(_context.CurrentDirectory, prefix));
    }

    /// <summary>
    /// Finds the start index of the word at the given cursor position.
    /// A word boundary is a space character.
    /// </summary>
    private static int FindWordStart(string input, int cursorPos)
    {
        var i = cursorPos - 1;
        while (i >= 0 && input[i] != ' ')
            i--;
        return i + 1;
    }

    /// <summary>
    /// Determines if the word at the given start position is the first word
    /// in the command (i.e., command position).
    /// </summary>
    private static bool IsFirstWord(string input, int wordStart)
    {
        for (var i = 0; i < wordStart; i++)
        {
            if (input[i] != ' ')
                return false;
        }
        return true;
    }

    /// <summary>
    /// Gets the command name (first word) from the input line.
    /// </summary>
    private static string GetCommandName(string input)
    {
        var trimmed = input.TrimStart();
        var end = 0;
        while (end < trimmed.Length && trimmed[end] != ' ')
            end++;
        return trimmed[..end];
    }

    /// <summary>
    /// Gets all executable names from PATH directories, using a cache.
    /// </summary>
    private List<string> GetPathExecutables()
    {
        // Use cache if fresh
        if (_pathExecutableCache is not null && DateTime.Now - _pathCacheTime < PathCacheTimeout)
            return _pathExecutableCache;

        var executables = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
            foreach (var dir in pathDirs)
            {
                try
                {
                    if (!Directory.Exists(dir))
                        continue;
                    foreach (var file in Directory.EnumerateFiles(dir))
                    {
                        var name = Path.GetFileName(file);
                        if (!string.IsNullOrEmpty(name))
                            executables.Add(name);
                    }
                }
                catch { /* ignore permission errors */ }
            }
        }
        catch { /* ignore */ }

        _pathExecutableCache = executables.ToList();
        _pathCacheTime = DateTime.Now;
        return _pathExecutableCache;
    }

    /// <summary>
    /// Applies tab completion results to the input buffer.
    /// Handles single match (complete), common prefix completion, and
    /// displaying multiple matches in columns.
    /// </summary>
    /// <param name="matches">The completion candidates.</param>
    /// <param name="sb">The input buffer.</param>
    /// <param name="cursorPos">The cursor position (may be updated).</param>
    /// <param name="left">The starting cursor column.</param>
    /// <param name="wordStart">The index where the current word starts in the buffer.</param>
    /// <param name="prefix">The text being completed.</param>
    private void ApplyCompletion(List<string> matches, StringBuilder sb, ref int cursorPos, ref int startLeft, int wordStart, string prefix)
    {
        if (matches.Count == 0)
            return;

        if (matches.Count == 1)
        {
            // Single match — complete it
            var completion = matches[0];
            sb.Remove(wordStart, cursorPos - wordStart);
            sb.Insert(wordStart, completion);
            cursorPos = wordStart + completion.Length;
            RedisplayLine(startLeft, sb, cursorPos);
        }
        else
        {
            // Multiple matches — find common prefix
            var commonPrefix = FindCommonPrefix(matches);
            if (commonPrefix.Length > prefix.Length)
            {
                sb.Remove(wordStart, cursorPos - wordStart);
                sb.Insert(wordStart, commonPrefix);
                cursorPos = wordStart + commonPrefix.Length;
                RedisplayLine(startLeft, sb, cursorPos);
            }
            else
            {
                // Show all matches in columns
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
                startLeft = Console.CursorLeft;
                Console.Write(sb.ToString());
                SetCursorPosition(startLeft, sb, cursorPos);
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

    // ═══════════════════════════════════════════════════════════════════════
    // Theme System
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Renders the prompt using the active theme.
    /// </summary>
    private string RenderThemePrompt()
    {
        var ctx = BuildPromptContext();
        var leftPrompt = _themeManager.RenderPrompt(ctx);
        var rightPrompt = _themeManager.RenderRightPrompt(ctx);

        if (string.IsNullOrEmpty(rightPrompt))
            return leftPrompt;

        return rightPrompt + leftPrompt;
    }

    /// <summary>
    /// Builds a PromptContext from the current shell state.
    /// </summary>
    private PromptContext BuildPromptContext()
    {
        return new PromptContext
        {
            User = Environment.GetEnvironmentVariable("USER") ?? Environment.UserName,
            Host = Environment.MachineName,
            HomeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Cwd = _context.CurrentDirectory,
            LastExitCode = _context.LastExitCode,
            IsRoot = Environment.UserName == "root",
            GitBranch = Prompt.GetGitBranch(),
            GitDirty = Prompt.IsGitDirty(),
            JobCount = 0,
            Now = DateTime.Now,
            ShellName = "radiance"
        };
    }

    /// <summary>
    /// Gets the active theme manager instance.
    /// </summary>
    public ThemeManager Themes => _themeManager;

    // ═══════════════════════════════════════════════════════════════════════
    // Initialization
    // ═══════════════════════════════════════════════════════════════════════

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
        Console.WriteLine("\x1b[37m  Type 'exit' to quit. Try \x1b[1;33m'radiance'\x1b[0m\x1b[37m for fun, \x1b[1;33m'agent'\x1b[0m\x1b[37m for AI help!\x1b[0m");
        Console.WriteLine();
    }
}