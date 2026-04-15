using System.Text;
using System.Text.RegularExpressions;
using Radiance.Interpreter;

namespace Radiance.Agent;

/// <summary>
/// The Lira AI agent — a friendly fennec-eared assistant that lives in the Radiance shell.
/// Handles the interactive chat loop, streaming responses from OpenAI-compatible APIs,
/// code block rendering with syntax highlighting, tool calling with user confirmation,
/// and conversation history management.
/// </summary>
public sealed partial class LiraAgent : IDisposable
{
    private readonly AgentConfig _config;
    private readonly OpenAiClient _client;
    private readonly AgentTools _tools;
    private readonly ShellContext _context;
    private readonly List<ChatMessage> _history = [];
    private readonly CancellationTokenSource _cts = new();
    private bool _running = true;

    /// <summary>
    /// Maximum lines to show in a code block before collapsing.
    /// </summary>
    private const int MaxVisibleCodeLines = 20;

    /// <summary>
    /// Lines to show at the top and bottom of a collapsed code block.
    /// </summary>
    private const int PreviewLines = 8;

    public LiraAgent(ShellContext context)
    {
        _context = context;
        _config = AgentConfig.Load();
        _client = new OpenAiClient(_config);
        _tools = new AgentTools(context);
    }

    /// <summary>
    /// Gets the current configuration.
    /// </summary>
    public AgentConfig Config => _config;

    /// <summary>
    /// Starts the interactive agent loop. Returns when the user exits.
    /// </summary>
    /// <returns>Exit code (0 for normal exit).</returns>
    public int Run()
    {
        // Check for API key
        if (!_config.HasApiKey)
        {
            PrintSetupHelp();
            return 1;
        }

        // Initialize conversation with system prompt
        _history.Clear();
        _history.Add(new ChatMessage
        {
            Role = "system",
            Content = _config.EffectiveSystemPrompt
        });

        // Add context message with working directory info
        _history.Add(new ChatMessage
        {
            Role = "system",
            Content = $"Current working directory: {_context.CurrentDirectory}\nUser: {Environment.UserName}\nHost: {Environment.MachineName}\nOS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}"
        });

        PrintWelcome();

        while (_running)
        {
            // Render Lira prompt
            Console.Write("\x1b[1;35mlira \x1b[1;33m✦\x1b[0m\x1b[1;35m>\x1b[0m ");

            var input = Console.ReadLine();

            if (input is null)
            {
                // Ctrl+D — EOF
                Console.WriteLine();
                break;
            }

            if (string.IsNullOrWhiteSpace(input))
                continue;

            input = input.Trim();

            // Handle in-chat commands
            if (HandleCommand(input))
                continue;

            // Send to API and process response
            ProcessUserMessage(input).GetAwaiter().GetResult();
        }

        return 0;
    }

    /// <summary>
    /// Handles in-chat slash commands.
    /// </summary>
    /// <param name="input">The raw input line.</param>
    /// <returns>True if the input was a command (and was handled).</returns>
    private bool HandleCommand(string input)
    {
        if (!input.StartsWith('/'))
            return false;

        var parts = input.Split(' ', 2);
        var cmd = parts[0].ToLowerInvariant();
        var arg = parts.Length > 1 ? parts[1].Trim() : "";

        switch (cmd)
        {
            case "/exit":
            case "/quit":
            case "/q":
                _running = false;
                Console.WriteLine("\x1b[1;35m  🦊 Bye! See you next time~\x1b[0m");
                return true;

            case "/reset":
                _history.Clear();
                _history.Add(new ChatMessage { Role = "system", Content = _config.EffectiveSystemPrompt });
                _history.Add(new ChatMessage
                {
                    Role = "system",
                    Content = $"Current working directory: {_context.CurrentDirectory}\nUser: {Environment.UserName}\nHost: {Environment.MachineName}\nOS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}"
                });
                Console.WriteLine("\x1b[1;35m  ✨ Conversation reset! Starting fresh.\x1b[0m");
                return true;

            case "/config":
                Console.WriteLine($"\x1b[1;36m  ── Lira Configuration ──\x1b[0m");
                Console.WriteLine(_config.DisplayString.Indent("  "));
                return true;

            case "/model":
                if (string.IsNullOrEmpty(arg))
                {
                    Console.WriteLine($"\x1b[1;36m  Current model:\x1b[0m {_config.Model}");
                }
                else
                {
                    _config.Model = arg;
                    Console.WriteLine($"\x1b[1;35m  🦊 Switched model to:\x1b[0m {arg}");
                }
                return true;

            case "/help":
                PrintHelp();
                return true;

            case "/history":
                var userMessages = _history.Count(m => m.Role == "user");
                var totalMessages = _history.Count;
                Console.WriteLine($"\x1b[1;36m  Messages in history:\x1b[0m {totalMessages} ({userMessages} user messages)");
                return true;

            default:
                Console.WriteLine($"\x1b[1;31m  Unknown command: {cmd}\x1b[0m");
                Console.WriteLine("\x1b[37m  Type /help for available commands.\x1b[0m");
                return true;
        }
    }

    /// <summary>
    /// Sends a user message to the API and processes the streaming response.
    /// Handles tool calls, code block rendering, and conversation history management.
    /// </summary>
    /// <param name="userInput">The user's input text.</param>
    private async Task ProcessUserMessage(string userInput)
    {
        // Add user message to history
        _history.Add(new ChatMessage { Role = "user", Content = userInput });

        // Trim history if it exceeds the limit
        TrimHistory();

        var toolDefinitions = _config.ToolsEnabled ? AgentTools.GetToolDefinitions() : null;

        try
        {
            await ProcessApiStream(toolDefinitions);
        }
        catch (OpenAiException ex)
        {
            Console.Error.WriteLine($"\x1b[1;31m  API Error: {ex.Message}\x1b[0m");
            Console.WriteLine();
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"\x1b[1;31m  Connection Error: {ex.Message}\x1b[0m");
            Console.WriteLine("\x1b[37m  Check your API base URL and network connection.\x1b[0m");
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("\x1b[1;33m  Request cancelled.\x1b[0m");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\x1b[1;31m  Error: {ex.Message}\x1b[0m");
        }
    }

    /// <summary>
    /// Processes the streaming API response, handling both text content and tool calls.
    /// Automatically re-sends to the API when tool calls are completed (agentic loop).
    /// </summary>
    /// <param name="toolDefinitions">Available tool definitions.</param>
    /// <param name="maxIterations">Maximum tool-use iterations to prevent infinite loops.</param>
    private async Task ProcessApiStream(List<ToolDefinition>? toolDefinitions, int maxIterations = 10)
    {
        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var contentBuilder = new StringBuilder();
            var toolCalls = new Dictionary<int, ToolCallBuilder>();

            await foreach (var chunk in _client.StreamChatAsync(_history, toolDefinitions, _cts.Token))
            {
                if (chunk.Choices.Count == 0)
                    continue;

                var choice = chunk.Choices[0];

                // Handle content delta
                if (choice.Delta.Content is { Length: > 0 })
                {
                    contentBuilder.Append(choice.Delta.Content);
                    // Stream output character by character
                    Console.Write(choice.Delta.Content);
                }

                // Handle tool call deltas
                if (choice.Delta.ToolCalls is { Count: > 0 })
                {
                    foreach (var tc in choice.Delta.ToolCalls)
                    {
                        if (!toolCalls.TryGetValue(tc.Index, out var builder))
                        {
                            builder = new ToolCallBuilder();
                            toolCalls[tc.Index] = builder;
                        }

                        if (tc.Id is not null)
                            builder.Id = tc.Id;
                        if (tc.Type is not null)
                            builder.Type = tc.Type;
                        if (tc.Function.Name is { Length: > 0 })
                            builder.FunctionName += tc.Function.Name;
                        if (tc.Function.Arguments is { Length: > 0 })
                            builder.ArgumentsBuilder.Append(tc.Function.Arguments);
                    }
                }

                // Check for finish — no special handling needed, the loop
                // continues to accumulate content/tool calls until the stream ends
                _ = choice.FinishReason;
            }

            Console.WriteLine(); // Newline after streaming

            var fullContent = contentBuilder.ToString();

            // Render code blocks in the content
            if (fullContent.Length > 0)
            {
                RenderCodeBlocks(fullContent);
            }

            // Build assistant message
            var assistantMessage = new ChatMessage
            {
                Role = "assistant",
                Content = string.IsNullOrEmpty(fullContent) ? null : fullContent
            };

            // If there are tool calls, add them to the message
            if (toolCalls.Count > 0)
            {
                assistantMessage.ToolCalls = toolCalls.Values
                    .OrderBy(b => b.Index)
                    .Select(b => new ToolCall
                    {
                        Id = b.Id,
                        Type = b.Type ?? "function",
                        Function = new ToolCallFunction
                        {
                            Name = b.FunctionName,
                            Arguments = b.ArgumentsBuilder.ToString()
                        }
                    }).ToList();
            }

            _history.Add(assistantMessage);

            // If no tool calls, we're done
            if (toolCalls.Count == 0)
                return;

            // Process tool calls
            foreach (var (_, tc) in toolCalls.OrderBy(kv => kv.Key))
            {
                var toolName = tc.FunctionName;
                var arguments = tc.ArgumentsBuilder.ToString();

                // Display the tool call
                var formattedCall = AgentTools.FormatToolCall(toolName, arguments);
                Console.WriteLine($"\x1b[1;36m  {formattedCall}\x1b[0m");

                // Ask for confirmation if destructive
                if (AgentTools.RequiresConfirmation(toolName))
                {
                    Console.Write("\x1b[1;33m  Allow? [y/N]: \x1b[0m");
                    var confirmation = Console.ReadLine()?.Trim().ToLowerInvariant();

                    if (confirmation != "y" && confirmation != "yes")
                    {
                        Console.WriteLine("\x1b[1;33m  ⏭ Skipped.\x1b[0m");
                        _history.Add(new ChatMessage
                        {
                            Role = "tool",
                            ToolCallId = tc.Id,
                            Name = toolName,
                            Content = "User denied permission to execute this tool."
                        });
                        continue;
                    }
                }

                // Execute the tool
                Console.Write("\x1b[37m");
                var result = _tools.Execute(toolName, arguments);
                Console.Write("\x1b[0m");

                // Display the result
                Console.WriteLine($"\x1b[38;5;245m{result.Indent("  ")}\x1b[0m");
                Console.WriteLine();

                // Add tool result to history
                _history.Add(new ChatMessage
                {
                    Role = "tool",
                    ToolCallId = tc.Id,
                    Name = toolName,
                    Content = result
                });
            }

            // Trim history again before the next iteration
            TrimHistory();

            // The loop continues — the API will see the tool results and can respond
        }

        // If we hit max iterations, let the user know
        Console.WriteLine("\x1b[1;33m  ⚠ Maximum tool iterations reached. Continuing the conversation normally.\x1b[0m");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Code Block Rendering
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Finds fenced code blocks in the response text and renders them with
    /// syntax highlighting, line numbers, and colored borders.
    /// Code blocks that were already streamed to the console are re-rendered
    /// in place with enhanced formatting.
    /// </summary>
    /// <param name="content">The full assistant response content.</param>
    private static void RenderCodeBlocks(string content)
    {
        // Find all code blocks with language tags
        var matches = CodeBlockRegex().Matches(content);

        foreach (Match match in matches)
        {
            var language = match.Groups[1].Value;
            var code = match.Groups[2].Value;

            // Skip empty code blocks
            if (string.IsNullOrWhiteSpace(code))
                continue;

            RenderSingleCodeBlock(language, code);
        }
    }

    /// <summary>
    /// Renders a single code block with syntax highlighting and line numbers.
    /// </summary>
    /// <param name="language">The programming language.</param>
    /// <param name="code">The code content.</param>
    private static void RenderSingleCodeBlock(string language, string code)
    {
        var lines = code.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var lineNumWidth = Math.Max(2, lines.Length.ToString().Length);

        // Top border with language name
        var headerWidth = Math.Max(40, Math.Min(Console.WindowWidth - 4, GetMaxLineLength(lines) + lineNumWidth + 8));
        var headerBar = new string('─', Math.Max(1, headerWidth - language.Length - 3));

        Console.WriteLine($"\x1b[1;36m  ╭─ \x1b[1;33m{language}\x1b[1;36m {headerBar}╮\x1b[0m");

        // Determine if we need to collapse
        var collapsed = lines.Length > MaxVisibleCodeLines;

        if (collapsed)
        {
            // Show first PreviewLines lines
            for (var i = 0; i < PreviewLines && i < lines.Length; i++)
            {
                PrintCodeLine(lines[i], i + 1, lineNumWidth);
            }

            // Show collapse indicator
            var hiddenCount = lines.Length - (PreviewLines * 2);
            Console.WriteLine($"\x1b[38;5;245m  │  ... {hiddenCount} lines hidden ...\x1b[0m");

            // Show last PreviewLines lines
            for (var i = lines.Length - PreviewLines; i < lines.Length; i++)
            {
                PrintCodeLine(lines[i], i + 1, lineNumWidth);
            }
        }
        else
        {
            foreach (var (line, idx) in lines.Select((l, i) => (l, i)))
            {
                PrintCodeLine(line, idx + 1, lineNumWidth);
            }
        }

        // Bottom border
        var footerBar = new string('─', headerWidth);
        Console.WriteLine($"\x1b[1;36m  ╰{footerBar}╯\x1b[0m");
    }

    /// <summary>
    /// Prints a single line of code with a line number and basic syntax highlighting.
    /// </summary>
    private static void PrintCodeLine(string line, int lineNum, int lineNumWidth)
    {
        var numStr = lineNum.ToString().PadLeft(lineNumWidth);
        var highlighted = SyntaxHighlight(line);

        // Truncate very long lines
        if (line.Length > Console.WindowWidth - lineNumWidth - 8)
        {
            var maxLen = Console.WindowWidth - lineNumWidth - 11;
            if (maxLen > 0)
                highlighted = SyntaxHighlight(line[..maxLen]) + "\x1b[38;5;245m...→\x1b[0m";
        }

        Console.WriteLine($"\x1b[1;36m  │\x1b[0m \x1b[38;5;245m{numStr}\x1b[1;36m │\x1b[0m {highlighted}");
    }

    /// <summary>
    /// Applies basic ANSI syntax highlighting to a line of code.
    /// </summary>
    private static string SyntaxHighlight(string line)
    {
        // Don't highlight if the line is too short
        if (line.Length < 2)
            return line;

        var result = new StringBuilder(line.Length + 20);
        var i = 0;

        while (i < line.Length)
        {
            var c = line[i];

            // Check for comments
            if ((c == '#' && i == 0) || (c == '/' && i + 1 < line.Length && line[i + 1] == '/'))
            {
                result.Append("\x1b[38;5;245m"); // Gray
                result.Append(line[i..]);
                result.Append("\x1b[0m");
                break;
            }

            // Check for strings
            if (c is '"' or '\'')
            {
                var quote = c;
                result.Append($"\x1b[1;32m{c}"); // Green
                i++;
                while (i < line.Length && line[i] != quote)
                {
                    if (line[i] == '\\' && i + 1 < line.Length)
                    {
                        result.Append(line[i]);
                        i++;
                    }
                    result.Append(line[i]);
                    i++;
                }
                if (i < line.Length)
                {
                    result.Append(line[i]);
                    i++;
                }
                result.Append("\x1b[0m");
                continue;
            }

            // Check for numbers
            if (char.IsDigit(c) && (i == 0 || !char.IsLetterOrDigit(line[i - 1])))
            {
                result.Append("\x1b[1;35m"); // Magenta
                while (i < line.Length && (char.IsDigit(line[i]) || line[i] is '.' or 'x' or 'b' or 'o' or 'a' or 'b' or 'c' or 'd' or 'e' or 'f'))
                {
                    result.Append(line[i]);
                    i++;
                }
                result.Append("\x1b[0m");
                continue;
            }

            // Check for keywords
            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_'))
                    i++;

                var word = line[start..i];

                if (IsKeyword(word))
                {
                    result.Append($"\x1b[1;34m{word}\x1b[0m"); // Blue
                }
                else
                {
                    result.Append(word);
                }
                continue;
            }

            // Check for operators
            if ("+-*/%=<>!&|^~?:;".Contains(c))
            {
                result.Append($"\x1b[1;36m{c}\x1b[0m"); // Cyan
                i++;
                continue;
            }

            // Default
            result.Append(c);
            i++;
        }

        return result.ToString();
    }

    /// <summary>
    /// Checks if a word is a common programming keyword.
    /// </summary>
    private static bool IsKeyword(string word) => Keywords.Contains(word);

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        // General
        "if", "else", "elif", "fi", "then", "for", "while", "until", "do", "done",
        "case", "esac", "in", "function", "return", "local", "export", "readonly",
        "switch", "default", "break", "continue", "goto", "yield", "await", "async",
        // C-style
        "int", "float", "double", "char", "void", "bool", "string", "var", "let",
        "const", "static", "final", "public", "private", "protected", "internal",
        "class", "struct", "interface", "enum", "namespace", "using", "import",
        "package", "module", "new", "delete", "this", "self", "super", "base",
        "true", "false", "null", "None", "nil", "undefined", "NaN",
        "try", "catch", "finally", "throw", "throws", "raise", "except",
        // Functional
        "def", "fn", "func", "fun", "lambda", "match", "with", "when",
        // Rust / Go
        "mut", "impl", "trait", "pub", "crate", "mod", "type", "where",
        "chan", "defer", "go", "range", "select", "map",
        // Python
        "and", "or", "not", "is", "as", "pass", "assert", "global", "nonlocal",
        "from", "elif",
        // Shell
        "echo", "exit", "set", "unset", "source", "alias", "unalias",
        "shift", "read", "eval", "exec", "trap", "wait",
        // Common functions
        "print", "println", "printf", "console", "log", "println",
        "require", "include", "extends", "implements"
    };

    /// <summary>
    /// Gets the maximum line length in a set of lines.
    /// </summary>
    private static int GetMaxLineLength(string[] lines)
    {
        var max = 0;
        foreach (var line in lines)
        {
            if (line.Length > max)
                max = line.Length;
        }
        return max;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Conversation Management
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Trims conversation history to stay within the configured max message limit.
    /// Always preserves the first system message.
    /// </summary>
    private void TrimHistory()
    {
        if (_history.Count <= _config.MaxHistory)
            return;

        // Keep the first system message and trim the oldest messages
        var toRemove = _history.Count - _config.MaxHistory;
        for (var i = 0; i < toRemove && _history.Count > 1; i++)
        {
            // Don't remove the first system message
            _history.RemoveAt(1);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Display Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Prints the Lira welcome banner.
    /// </summary>
    private static void PrintWelcome()
    {
        Console.WriteLine();
        Console.WriteLine("\x1b[1;35m  ╭──────────────────────────────────────╮\x1b[0m");
        Console.WriteLine("\x1b[1;35m  │  \x1b[1;33m🦊 Lira — AI Assistant \x1b[1;35m            │\x1b[0m");
        Console.WriteLine("\x1b[1;35m  │  \x1b[37mYour fennec-eared coding companion \x1b[1;35m  │\x1b[0m");
        Console.WriteLine("\x1b[1;35m  ╰──────────────────────────────────────╯\x1b[0m");
        Console.WriteLine("\x1b[37m  Type /help for commands, /exit to leave.\x1b[0m");
        Console.WriteLine();
    }

    /// <summary>
    /// Prints the help text for in-chat commands.
    /// </summary>
    private static void PrintHelp()
    {
        Console.WriteLine();
        Console.WriteLine("\x1b[1;36m  ── Lira Commands ──\x1b[0m");
        Console.WriteLine("\x1b[1;33m  /help\x1b[0m      Show this help message");
        Console.WriteLine("\x1b[1;33m  /exit\x1b[0m      Exit Lira (also /quit, /q)");
        Console.WriteLine("\x1b[1;33m  /reset\x1b[0m     Reset conversation history");
        Console.WriteLine("\x1b[1;33m  /config\x1b[0m    Show current API configuration");
        Console.WriteLine("\x1b[1;33m  /model\x1b[0m     Show or change model (/model gpt-4o)");
        Console.WriteLine("\x1b[1;33m  /history\x1b[0m   Show message count in history");
        Console.WriteLine();
        Console.WriteLine("\x1b[37m  Just type your message to chat with Lira.\x1b[0m");
        Console.WriteLine("\x1b[37m  Lira can run commands, read/write files, and more!\x1b[0m");
        Console.WriteLine();
    }

    /// <summary>
    /// Prints setup instructions when no API key is configured.
    /// </summary>
    private static void PrintSetupHelp()
    {
        Console.WriteLine();
        Console.WriteLine("\x1b[1;31m  🦊 Lira needs an API key to work!\x1b[0m");
        Console.WriteLine();
        Console.WriteLine("\x1b[1;36m  ── Setup ──\x1b[0m");
        Console.WriteLine();
        Console.WriteLine("  Set an environment variable:");
        Console.WriteLine("\x1b[1;33m    export LIRA_API_KEY=\"your-api-key-here\"\x1b[0m");
        Console.WriteLine();
        Console.WriteLine("  Or create \x1b[1;36m~/.radiance/agent.json\x1b[0m:");
        Console.WriteLine("\x1b[1;33m    {");
        Console.WriteLine("      \"api_key\": \"your-api-key\",");
        Console.WriteLine("      \"base_url\": \"https://api.openai.com/v1\",");
        Console.WriteLine("      \"model\": \"gpt-4o\"");
        Console.WriteLine("    }\x1b[0m");
        Console.WriteLine();
        Console.WriteLine("\x1b[37m  Works with any OpenAI-compatible API:");
        Console.WriteLine("  OpenAI, Ollama, LM Studio, vLLM, etc.\x1b[0m");
        Console.WriteLine();
        Console.WriteLine("  Environment variables:");
        Console.WriteLine("    \x1b[1;33mLIRA_API_KEY\x1b[0m / \x1b[1;33mOPENAI_API_KEY\x1b[0m    — API key");
        Console.WriteLine("    \x1b[1;33mLIRA_BASE_URL\x1b[0m / \x1b[1;33mOPENAI_BASE_URL\x1b[0m  — API base URL");
        Console.WriteLine("    \x1b[1;33mLIRA_MODEL\x1b[0m                 — Model name");
        Console.WriteLine();
    }

    /// <summary>
    /// Regex for matching fenced code blocks with optional language tags.
    /// </summary>
    [GeneratedRegex(@"```(\w*)\n(.*?)```", RegexOptions.Singleline)]
    private static partial Regex CodeBlockRegex();

    /// <summary>
    /// Helper class for accumulating tool call data from streaming chunks.
    /// </summary>
    private sealed class ToolCallBuilder
    {
        public int Index { get; set; }
        public string Id { get; set; } = "";
        public string Type { get; set; } = "function";
        public string FunctionName { get; set; } = "";
        public StringBuilder ArgumentsBuilder { get; } = new();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _client.Dispose();
    }
}

/// <summary>
/// String extension methods for the agent.
/// </summary>
internal static class LiraStringExtensions
{
    /// <summary>
    /// Indents each line of a string with the given prefix.
    /// </summary>
    public static string Indent(this string s, string prefix)
    {
        if (string.IsNullOrEmpty(s))
            return s;

        var lines = s.Split('\n');
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            if (string.IsNullOrEmpty(line))
                sb.AppendLine();
            else
                sb.AppendLine($"{prefix}{line}");
        }
        return sb.ToString().TrimEnd('\n');
    }
}