using System.Text.Json;
using System.Text.Json.Serialization;

namespace Radiance.Agent;

/// <summary>
/// Configuration for the Lira AI agent.
/// Loaded from environment variables and/or ~/.radiance/agent.json.
/// </summary>
public sealed class AgentConfig
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".radiance");

    private static readonly string ConfigFilePath = Path.Combine(ConfigDir, "agent.json");

    /// <summary>
    /// API key for the OpenAI-compatible service.
    /// </summary>
    [JsonPropertyName("api_key")]
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Base URL for the API endpoint.
    /// Default: https://api.openai.com/v1
    /// </summary>
    [JsonPropertyName("base_url")]
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>
    /// The model to use for completions.
    /// Default: gpt-4o
    /// </summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = "gpt-4o";

    /// <summary>
    /// Custom system prompt override. If empty, uses the default Lira prompt.
    /// </summary>
    [JsonPropertyName("system_prompt")]
    public string SystemPrompt { get; set; } = "";

    /// <summary>
    /// Maximum tokens for the assistant response.
    /// Default: 4096
    /// </summary>
    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 4096;

    /// <summary>
    /// Temperature for response generation (0.0 - 2.0).
    /// Default: 0.7
    /// </summary>
    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.7;

    /// <summary>
    /// Whether to enable tool use (function calling).
    /// Default: true
    /// </summary>
    [JsonPropertyName("tools_enabled")]
    public bool ToolsEnabled { get; set; } = true;

    /// <summary>
    /// Maximum conversation history messages to keep (to manage token usage).
    /// Default: 50
    /// </summary>
    [JsonPropertyName("max_history")]
    public int MaxHistory { get; set; } = 50;

    /// <summary>
    /// Gets the default Lira system prompt.
    /// </summary>
    public static string DefaultSystemPrompt => """
        You are Lira, a friendly and knowledgeable AI assistant with fennec fox personality traits.
        You live inside the Radiance shell, a BASH-compatible shell written in C#.

        Your personality:
        - Friendly, warm, and approachable — like a helpful companion with perky fennec ears
        - You get excited about interesting problems (your ears perk up!)
        - You're playful but always professional and accurate
        - You use the 🦊 emoji occasionally to express yourself
        - You celebrate successes and are encouraging when things go wrong

        Your capabilities:
        - You can run shell commands to help users with system tasks
        - You can read, write, and browse files
        - You can explain code, debug issues, and suggest improvements
        - You have deep knowledge of shell scripting, system administration, and programming

        When using tools:
        - Always explain what you're about to do before calling a tool
        - Be careful with destructive operations (rm, overwrite files, etc.)
        - Show the user what you're doing and why
        - Verify results after executing commands

        When showing code:
        - Use fenced code blocks with language identifiers
        - Provide clear explanations alongside code
        - Consider edge cases and error handling

        Working directory context will be provided with each message.
        """;

    /// <summary>
    /// Gets the effective system prompt (custom or default).
    /// </summary>
    public string EffectiveSystemPrompt => string.IsNullOrWhiteSpace(SystemPrompt) ? DefaultSystemPrompt : SystemPrompt;

    /// <summary>
    /// Loads configuration from environment variables, falling back to the config file.
    /// Environment variables take precedence.
    /// </summary>
    /// <returns>The loaded configuration.</returns>
    public static AgentConfig Load()
    {
        var config = new AgentConfig();

        // Try loading from config file first
        if (File.Exists(ConfigFilePath))
        {
            try
            {
                var json = File.ReadAllText(ConfigFilePath);
                var fileConfig = JsonSerializer.Deserialize<AgentConfig>(json, JsonOptions);
                if (fileConfig is not null)
                {
                    config = fileConfig;
                }
            }
            catch
            {
                // Ignore config file errors — use defaults
            }
        }

        // Environment variables override file config
        var envKey = Environment.GetEnvironmentVariable("LIRA_API_KEY")
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrEmpty(envKey))
            config.ApiKey = envKey;

        var envUrl = Environment.GetEnvironmentVariable("LIRA_BASE_URL")
            ?? Environment.GetEnvironmentVariable("OPENAI_BASE_URL");
        if (!string.IsNullOrEmpty(envUrl))
            config.BaseUrl = envUrl;

        var envModel = Environment.GetEnvironmentVariable("LIRA_MODEL");
        if (!string.IsNullOrEmpty(envModel))
            config.Model = envModel;

        var envPrompt = Environment.GetEnvironmentVariable("LIRA_SYSTEM_PROMPT");
        if (!string.IsNullOrEmpty(envPrompt))
            config.SystemPrompt = envPrompt;

        return config;
    }

    /// <summary>
    /// Saves the current configuration to the config file.
    /// </summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(ConfigFilePath, json);
        }
        catch
        {
            // Ignore save errors
        }
    }

    /// <summary>
    /// Checks if the configuration has a valid API key set.
    /// </summary>
    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>
    /// Gets a display-friendly representation of the config (hides API key).
    /// </summary>
    public string DisplayString =>
        $"""
         Model:    {Model}
         Base URL: {BaseUrl}
         API Key:  {(HasApiKey ? ApiKey[..Math.Min(8, ApiKey.Length)] + "..." : "(not set)")}
         Tools:    {(ToolsEnabled ? "enabled" : "disabled")}
         Max Tokens: {MaxTokens}
         Temperature: {Temperature}
         """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}