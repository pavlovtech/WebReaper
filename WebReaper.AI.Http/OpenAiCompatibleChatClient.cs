using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace WebReaper.AI.Http;

/// <summary>
/// An AOT-clean <see cref="IChatClient"/> that speaks the OpenAI Chat
/// Completions protocol over a raw <see cref="HttpClient"/> (ADR-0084).
/// No provider SDK: the request/response shapes go through System.Text.Json
/// source generation (<see cref="OpenAiJsonContext"/>), so the client
/// composes into a Native-AOT binary such as the WebReaper CLI.
/// <para>
/// Point the base URL at any OpenAI-compatible
/// <c>/chat/completions</c> endpoint: OpenAI (<c>https://api.openai.com/v1</c>),
/// Ollama (<c>http://localhost:11434/v1</c>), OpenRouter, vLLM, LM Studio, or
/// an Anthropic-compatible gateway. Hand the instance to WebReaper's
/// <c>WithLlmExtractor</c> / <c>WithLlmSchemaInferrer</c>.
/// </para>
/// <para>
/// Scope (ADR-0084 piece 2): JSON-mode chat completions, which is all the
/// <c>--prompt</c> / <c>--infer</c> extraction paths need. Tool calling
/// (the agent / action-resolver path) throws <see cref="NotSupportedException"/>
/// rather than silently dropping the tools.
/// </para>
/// </summary>
public sealed class OpenAiCompatibleChatClient : IChatClient
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly Uri _endpoint;
    private readonly string _defaultModel;
    private readonly string? _apiKey;

    /// <summary>Construct the client.</summary>
    /// <param name="baseUrl">The endpoint base, e.g.
    /// <c>https://api.openai.com/v1</c> or <c>http://localhost:11434/v1</c>.
    /// <c>/chat/completions</c> is appended.</param>
    /// <param name="model">The default model id, used when a per-call
    /// <see cref="ChatOptions.ModelId"/> is not supplied.</param>
    /// <param name="apiKey">Optional bearer token. Omitted (e.g. local
    /// Ollama) when null or empty, so no <c>Authorization</c> header is sent.</param>
    /// <param name="httpClient">Optional <see cref="HttpClient"/> to reuse.
    /// When null, the client owns a private instance and disposes it.</param>
    public OpenAiCompatibleChatClient(
        string baseUrl,
        string model,
        string? apiKey = null,
        HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        _endpoint = new Uri(baseUrl.TrimEnd('/') + "/chat/completions");
        _defaultModel = model;
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
        _http = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
    }

    /// <inheritdoc/>
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (options?.Tools is { Count: > 0 })
        {
            throw new NotSupportedException(
                "OpenAiCompatibleChatClient does not support tool calling (ADR-0084 " +
                "piece 2 ships JSON-mode chat completions for --prompt / --infer). " +
                "Use a tool-calling-capable IChatClient for the agent or action resolver.");
        }

        var request = new ChatCompletionRequest
        {
            Model = options?.ModelId ?? _defaultModel,
            Messages = BuildMessages(messages),
            Temperature = options?.Temperature,
            MaxTokens = options?.MaxOutputTokens,
            ResponseFormat = options?.ResponseFormat is ChatResponseFormatJson
                ? new ResponseFormatSpec { Type = "json_object" }
                : null,
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        var payload = JsonSerializer.Serialize(request, OpenAiJsonContext.Default.ChatCompletionRequest);
        httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        if (_apiKey is not null)
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        using var httpResponse = await _http
            .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!httpResponse.IsSuccessStatusCode)
        {
            var body = await ReadBodySafeAsync(httpResponse, cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"OpenAI-compatible endpoint {_endpoint} returned " +
                $"{(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}. {Truncate(body, 600)}");
        }

        ChatCompletionResponse? completion;
        await using (var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            completion = await JsonSerializer
                .DeserializeAsync(stream, OpenAiJsonContext.Default.ChatCompletionResponse, cancellationToken)
                .ConfigureAwait(false);
        }

        if (completion?.Choices is not { Count: > 0 } choices || choices[0].Message is null)
        {
            throw new InvalidOperationException(
                $"OpenAI-compatible endpoint {_endpoint} returned no choices.");
        }

        var content = choices[0].Message!.Content ?? string.Empty;
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, content))
        {
            ModelId = completion.Model ?? request.Model,
        };

        if (completion.Usage is { } usage)
        {
            response.Usage = new UsageDetails
            {
                InputTokenCount = usage.PromptTokens,
                OutputTokenCount = usage.CompletionTokens,
                TotalTokenCount = usage.TotalTokens,
            };
        }

        return response;
    }

    /// <summary>Not implemented: WebReaper's LLM adapters use the
    /// non-streaming <see cref="GetResponseAsync"/>. Throws
    /// <see cref="NotSupportedException"/>.</summary>
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "OpenAiCompatibleChatClient does not implement streaming; use GetResponseAsync.");

    /// <inheritdoc/>
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    private static List<ChatCompletionRequestMessage> BuildMessages(IEnumerable<ChatMessage> messages)
    {
        var result = new List<ChatCompletionRequestMessage>();
        foreach (var message in messages)
        {
            result.Add(new ChatCompletionRequestMessage
            {
                // ChatRole.Value is the canonical "system" / "user" /
                // "assistant" / "tool" string the protocol expects.
                Role = message.Role.Value,
                Content = message.Text ?? string.Empty,
            });
        }

        return result;
    }

    private static async Task<string> ReadBodySafeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "...";
}
