using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Radiance.Agent;

/// <summary>
/// HTTP client for OpenAI-compatible chat completions API.
/// Supports streaming responses and function/tool calling.
/// </summary>
public sealed class OpenAiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly AgentConfig _config;

    public OpenAiClient(AgentConfig config)
    {
        _config = config;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    /// <summary>
    /// Sends a chat completion request with streaming.
    /// Returns an async enumerable of streaming chunks.
    /// </summary>
    /// <param name="messages">The conversation messages.</param>
    /// <param name="tools">The available tools (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A stream of <see cref="ChatStreamChunk"/> objects.</returns>
    public async IAsyncEnumerable<ChatStreamChunk> StreamChatAsync(
        List<ChatMessage> messages,
        List<ToolDefinition>? tools = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var requestBody = BuildRequestBody(messages, tools, stream: true);
        var response = await SendRequestAsync(requestBody, cancellationToken);

        await foreach (var chunk in ParseSseStreamAsync(response, cancellationToken))
        {
            yield return chunk;
        }
    }

    /// <summary>
    /// Sends a non-streaming chat completion request.
    /// Returns the complete response.
    /// </summary>
    /// <param name="messages">The conversation messages.</param>
    /// <param name="tools">The available tools (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The complete chat response.</returns>
    public async Task<ChatResponse> ChatAsync(
        List<ChatMessage> messages,
        List<ToolDefinition>? tools = null,
        CancellationToken cancellationToken = default)
    {
        var requestBody = BuildRequestBody(messages, tools, stream: false);
        var response = await SendRequestAsync(requestBody, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new OpenAiException($"API error {response.StatusCode}: {body}");
        }

        return JsonSerializer.Deserialize<ChatResponse>(body, JsonOptions)
            ?? throw new OpenAiException("Failed to deserialize response");
    }

    /// <summary>
    /// Tests the API connection by sending a minimal request.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the connection is successful.</returns>
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var messages = new List<ChatMessage>
            {
                new() { Role = "user", Content = "Hi" }
            };

            var requestBody = BuildRequestBody(messages, null, stream: false);

            var response = await SendRequestAsync(requestBody, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the API key from the config.
    /// </summary>
    public string ApiKey => _config.ApiKey;

    /// <summary>
    /// Gets the model name from the config.
    /// </summary>
    public string Model => _config.Model;

    // ──── Private Helpers ────

    private string BuildRequestBody(List<ChatMessage> messages, List<ToolDefinition>? tools, bool stream)
    {
        var body = new Dictionary<string, object>
        {
            ["model"] = _config.Model,
            ["messages"] = messages,
            ["max_tokens"] = _config.MaxTokens,
            ["temperature"] = _config.Temperature,
            ["stream"] = stream
        };

        if (tools is { Count: > 0 } && _config.ToolsEnabled)
        {
            body["tools"] = tools;
            body["tool_choice"] = "auto";
        }

        return JsonSerializer.Serialize(body, JsonOptions);
    }

    private async Task<HttpResponseMessage> SendRequestAsync(string requestBody, CancellationToken cancellationToken)
    {
        var url = $"{_config.BaseUrl.TrimEnd('/')}/chat/completions";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        return response;
    }

    private static async IAsyncEnumerable<ChatStreamChunk> ParseSseStreamAsync(
        HttpResponseMessage response,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line))
                continue;

            if (!line.StartsWith("data: "))
                continue;

            var data = line[6..].Trim();
            if (data == "[DONE]")
                break;

            ChatStreamChunk? chunk = null;
            try
            {
                chunk = JsonSerializer.Deserialize<ChatStreamChunk>(data, JsonOptions);
            }
            catch
            {
                // Skip malformed chunks
                continue;
            }

            if (chunk is not null)
                yield return chunk;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };
}

// ═══════════════════════════════════════════════════════════════════════
// Data Models
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
/// A chat message in the conversation.
/// </summary>
public sealed class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<ToolCall>? ToolCalls { get; set; }

    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>
/// A tool call from the assistant.
/// </summary>
public sealed class ToolCall
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public ToolCallFunction Function { get; set; } = new();
}

/// <summary>
/// The function details of a tool call.
/// </summary>
public sealed class ToolCallFunction
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = "{}";
}

/// <summary>
/// A streaming chunk from the SSE response.
/// </summary>
public sealed class ChatStreamChunk
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("object")]
    public string Object { get; set; } = "";

    [JsonPropertyName("choices")]
    public List<StreamChoice> Choices { get; set; } = [];
}

/// <summary>
/// A choice within a streaming chunk.
/// </summary>
public sealed class StreamChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("delta")]
    public StreamDelta Delta { get; set; } = new();

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

/// <summary>
/// The delta content within a streaming choice.
/// </summary>
public sealed class StreamDelta
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<StreamToolCall>? ToolCalls { get; set; }
}

/// <summary>
/// A tool call delta within a streaming chunk.
/// </summary>
public sealed class StreamToolCall
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("function")]
    public StreamToolCallFunction Function { get; set; } = new();
}

/// <summary>
/// Function details within a streaming tool call.
/// </summary>
public sealed class StreamToolCallFunction
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }
}

/// <summary>
/// A complete (non-streaming) chat response.
/// </summary>
public sealed class ChatResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("choices")]
    public List<ChatChoice> Choices { get; set; } = [];
}

/// <summary>
/// A choice within a complete chat response.
/// </summary>
public sealed class ChatChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("message")]
    public ChatMessage Message { get; set; } = new();

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

/// <summary>
/// A tool definition for function calling.
/// </summary>
public sealed class ToolDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public ToolFunction Function { get; set; } = new();
}

/// <summary>
/// Function definition within a tool.
/// </summary>
public sealed class ToolFunction
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("parameters")]
    public ToolParameters? Parameters { get; set; }
}

/// <summary>
/// Parameters schema for a tool function.
/// </summary>
public sealed class ToolParameters
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";

    [JsonPropertyName("properties")]
    public Dictionary<string, ToolProperty> Properties { get; set; } = new();

    [JsonPropertyName("required")]
    public List<string> Required { get; set; } = [];
}

/// <summary>
/// A single property in a tool's parameters schema.
/// </summary>
public sealed class ToolProperty
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// Exception thrown when the OpenAI API returns an error.
/// </summary>
public sealed class OpenAiException : Exception
{
    public OpenAiException(string message) : base(message) { }
    public OpenAiException(string message, Exception inner) : base(message, inner) { }
}