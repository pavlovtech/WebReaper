using System.Text.Json.Serialization;

namespace WebReaper.AI.Http;

// ADR-0084: the OpenAI Chat Completions wire DTOs and their source-gen
// context. Source generation (not reflection) is what keeps the client
// Native-AOT clean. snake_case maps Model -> "model", MaxTokens ->
// "max_tokens", ResponseFormat -> "response_format", PromptTokens ->
// "prompt_tokens", and so on, so the DTO members stay idiomatic C#.

internal sealed class ChatCompletionRequest
{
    public string Model { get; set; } = string.Empty;
    public List<ChatCompletionRequestMessage> Messages { get; set; } = [];
    public float? Temperature { get; set; }
    public int? MaxTokens { get; set; }
    public ResponseFormatSpec? ResponseFormat { get; set; }
}

internal sealed class ChatCompletionRequestMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

internal sealed class ResponseFormatSpec
{
    public string Type { get; set; } = string.Empty;
}

internal sealed class ChatCompletionResponse
{
    public string? Model { get; set; }
    public List<ChatCompletionChoice>? Choices { get; set; }
    public ChatCompletionUsage? Usage { get; set; }
}

internal sealed class ChatCompletionChoice
{
    public ChatCompletionResponseMessage? Message { get; set; }
    public string? FinishReason { get; set; }
}

internal sealed class ChatCompletionResponseMessage
{
    public string? Content { get; set; }
}

internal sealed class ChatCompletionUsage
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ChatCompletionRequest))]
[JsonSerializable(typeof(ChatCompletionResponse))]
internal sealed partial class OpenAiJsonContext : JsonSerializerContext;
