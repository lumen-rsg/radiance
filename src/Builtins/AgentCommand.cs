using Radiance.Agent;
using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// The <c>agent</c> builtin command — launches the Lira AI assistant.
/// 
/// Usage:
///   agent          — Start interactive Lira chat session
///   agent config   — Show current API configuration
///   agent setup    — Show setup instructions
///   agent help     — Show usage information
/// </summary>
public sealed class AgentCommand : IBuiltinCommand
{
    /// <summary>
    /// The command name.
    /// </summary>
    public string Name => "agent";

    /// <summary>
    /// Executes the agent command.
    /// </summary>
    /// <param name="args">The arguments (args[0] = "agent").</param>
    /// <param name="context">The current shell context.</param>
    /// <returns>Exit code (0 for success, 1 for failure).</returns>
    public int Execute(string[] args, ShellContext context)
    {
        if (args.Length <= 1)
        {
            // No subcommand — launch interactive Lira session
            return RunAgent(context);
        }

        var subcommand = args[1].ToLowerInvariant();

        switch (subcommand)
        {
            case "config":
                return ShowConfig();

            case "setup":
                return ShowSetup();

            case "help":
            case "--help":
            case "-h":
                ShowHelp();
                return 0;

            default:
                Console.Error.WriteLine($"agent: unknown subcommand '{args[1]}'");
                Console.Error.WriteLine("Type 'agent help' for usage information.");
                return 1;
        }
    }

    /// <summary>
    /// Launches the interactive Lira agent session.
    /// Saves and restores the console state.
    /// </summary>
    /// <param name="context">The shell context.</param>
    /// <returns>Exit code.</returns>
    private static int RunAgent(ShellContext context)
    {
        using var agent = new LiraAgent(context);
        return agent.Run();
    }

    /// <summary>
    /// Shows the current API configuration.
    /// </summary>
    /// <returns>Exit code.</returns>
    private static int ShowConfig()
    {
        var config = AgentConfig.Load();

        Console.WriteLine("\x1b[1;36m── Lira Agent Configuration ──\x1b[0m");
        Console.WriteLine(config.DisplayString);

        if (!config.HasApiKey)
        {
            Console.WriteLine("\x1b[1;33m⚠ No API key configured. Run 'agent setup' for instructions.\x1b[0m");
        }

        return 0;
    }

    /// <summary>
    /// Shows setup instructions for configuring the agent.
    /// </summary>
    /// <returns>Exit code.</returns>
    private static int ShowSetup()
    {
        Console.WriteLine();
        Console.WriteLine("\x1b[1;36m── Lira Agent Setup ──\x1b[0m");
        Console.WriteLine();
        Console.WriteLine("Lira connects to any OpenAI-compatible API.");
        Console.WriteLine();
        Console.WriteLine("\x1b[1;33mOption 1: Environment variable\x1b[0m");
        Console.WriteLine("  export LIRA_API_KEY=\"your-api-key-here\"");
        Console.WriteLine("  export LIRA_BASE_URL=\"https://api.openai.com/v1\"  # optional");
        Console.WriteLine("  export LIRA_MODEL=\"gpt-4o\"                        # optional");
        Console.WriteLine();
        Console.WriteLine("\x1b[1;33mOption 2: Configuration file (~/.radiance/agent.json)\x1b[0m");
        Console.WriteLine("  {");
        Console.WriteLine("    \"api_key\": \"your-api-key\",");
        Console.WriteLine("    \"base_url\": \"https://api.openai.com/v1\",");
        Console.WriteLine("    \"model\": \"gpt-4o\",");
        Console.WriteLine("    \"temperature\": 0.7,");
        Console.WriteLine("    \"max_tokens\": 4096,");
        Console.WriteLine("    \"tools_enabled\": true");
        Console.WriteLine("  }");
        Console.WriteLine();
        Console.WriteLine("\x1b[1;33mCompatible APIs:\x1b[0m");
        Console.WriteLine("  • OpenAI (https://api.openai.com/v1)");
        Console.WriteLine("  • Ollama (http://localhost:11434/v1)");
        Console.WriteLine("  • LM Studio (http://localhost:1234/v1)");
        Console.WriteLine("  • vLLM, Together AI, Groq, and any OpenAI-compatible endpoint");
        Console.WriteLine();
        Console.WriteLine("\x1b[1;33mEnvironment variables:\x1b[0m");
        Console.WriteLine("  LIRA_API_KEY / OPENAI_API_KEY    — API key");
        Console.WriteLine("  LIRA_BASE_URL / OPENAI_BASE_URL  — API base URL");
        Console.WriteLine("  LIRA_MODEL                       — Model name");
        Console.WriteLine("  LIRA_SYSTEM_PROMPT               — Custom system prompt");
        Console.WriteLine();

        return 0;
    }

    /// <summary>
    /// Shows the help text.
    /// </summary>
    private static void ShowHelp()
    {
        Console.WriteLine();
        Console.WriteLine("\x1b[1;36m── Agent (Lira) ─ AI Assistant ──\x1b[0m");
        Console.WriteLine();
        Console.WriteLine("\x1b[1;33mUsage:\x1b[0m");
        Console.WriteLine("  agent          Start interactive Lira chat session");
        Console.WriteLine("  agent config   Show current API configuration");
        Console.WriteLine("  agent setup    Show setup instructions");
        Console.WriteLine("  agent help     Show this help message");
        Console.WriteLine();
        Console.WriteLine("\x1b[1;33mIn-Chat Commands:\x1b[0m");
        Console.WriteLine("  /help          Show available commands");
        Console.WriteLine("  /exit          Exit Lira (also /quit, /q)");
        Console.WriteLine("  /reset         Reset conversation history");
        Console.WriteLine("  /config        Show API configuration");
        Console.WriteLine("  /model <name>  Switch model mid-session");
        Console.WriteLine("  /history       Show message count");
        Console.WriteLine();
        Console.WriteLine("\x1b[37mLira is a friendly fennec-eared AI assistant that can run commands,");
        Console.WriteLine("read and write files, and help you with programming tasks.🦊\x1b[0m");
        Console.WriteLine();
    }
}