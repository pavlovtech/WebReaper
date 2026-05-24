using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WebReaper.AI.Llm;

/// <summary>
/// The mechanism module the four <c>WebReaper.AI</c> LLM adapters share
/// (ADR-0059). Owns: prompt marshalling, <see cref="IChatClient"/> transport,
/// code-fence stripping, the bounded one-shot parse-retry, tool-call dispatch
/// (the ADR-0060 seam), and <c>ChatResponse.Usage.TotalTokenCount</c> capture.
/// Does NOT own: prompt content, response shape, domain-type construction —
/// those live in the per-role <see cref="LlmCallDescriptor{TResponse}"/>.
/// <para>
/// Construct one per adapter (the descriptor + chat client pair is invariant);
/// call <see cref="InvokeAsync"/> per page / per intent / per state.
/// </para>
/// </summary>
/// <typeparam name="TResponse">The role's domain type — the descriptor's
/// <see cref="LlmCallDescriptor{TResponse}.ParseResponse"/> (or
/// <see cref="LlmCallDescriptor{TResponse}.ParseToolCall"/>) returns this.</typeparam>
public sealed class LlmCall<TResponse>
{
    /// <summary>The reminder appended to the user message on a parse-
    /// retry. Bounded at 1 retry — the second failure becomes an
    /// <see cref="LlmCallException"/>.</summary>
    internal const string ParseRetryReminder =
        "Your previous reply was not valid JSON. " +
        "Reply with valid JSON only. Do not wrap in code fences. " +
        "Do not include commentary, prefixes, or trailing prose.";

    /// <summary>The reminder appended on tool-call retry — the model
    /// must call exactly one tool.</summary>
    internal const string ToolCallRetryReminder =
        "Your previous reply did not call exactly one tool. " +
        "Call exactly one of the provided tools.";

    private readonly IChatClient _chatClient;
    private readonly LlmCallDescriptor<TResponse> _descriptor;
    private readonly ILogger _logger;

    /// <summary>Construct an <see cref="LlmCall{TResponse}"/>.</summary>
    /// <param name="chatClient">The Microsoft.Extensions.AI chat client
    /// (ADR-0009 quarantine — the consumer brings their own concrete
    /// implementation: OpenAI, Anthropic via wrapper, Ollama, …).</param>
    /// <param name="descriptor">The per-role policy record.</param>
    /// <param name="logger">Optional logger; <see cref="NullLogger{T}.Instance"/>
    /// when omitted.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="chatClient"/>
    /// or <paramref name="descriptor"/> is null.</exception>
    /// <exception cref="ArgumentException">When the descriptor's tool-call
    /// fields are misconfigured (<see cref="LlmCallDescriptor{TResponse}.Tools"/>
    /// set but <see cref="LlmCallDescriptor{TResponse}.ParseToolCall"/> not, or
    /// vice versa).</exception>
    public LlmCall(
        IChatClient chatClient,
        LlmCallDescriptor<TResponse> descriptor,
        ILogger<LlmCall<TResponse>>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.Tools is { Count: > 0 } && descriptor.ParseToolCall is null)
        {
            throw new ArgumentException(
                "LlmCallDescriptor has Tools set but no ParseToolCall delegate. " +
                "Tool-call mode requires both.", nameof(descriptor));
        }
        if (descriptor.ParseToolCall is not null && (descriptor.Tools is null || descriptor.Tools.Count == 0))
        {
            throw new ArgumentException(
                "LlmCallDescriptor has ParseToolCall set but no Tools. " +
                "Tool-call mode requires both.", nameof(descriptor));
        }

        _chatClient = chatClient;
        _descriptor = descriptor;
        _logger = (ILogger?)logger ?? NullLogger<LlmCall<TResponse>>.Instance;
    }

    /// <summary>Invoke the model with the descriptor's policies applied
    /// to <paramref name="input"/>.</summary>
    /// <param name="input">The adapter's input — passed verbatim to the
    /// descriptor's <see cref="LlmCallDescriptor{TResponse}.BuildUserMessage"/>.</param>
    /// <param name="cancellationToken">Threaded to <see cref="IChatClient.GetResponseAsync"/>.</param>
    /// <returns>An <see cref="LlmCallResult{TResponse}"/> carrying the
    /// parsed value, token usage (when surfaced), raw response, and
    /// retry count.</returns>
    /// <exception cref="LlmCallException">After the bounded retry fails.</exception>
    public async ValueTask<LlmCallResult<TResponse>> InvokeAsync(
        object input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var isToolCall = _descriptor.Tools is { Count: > 0 };
        var userMessage = _descriptor.BuildUserMessage(input);

        // First attempt.
        var (response, raw) = await CallAsync(userMessage, includeRetryReminder: false, cancellationToken).ConfigureAwait(false);
        var totalTokens = response.Usage?.TotalTokenCount;

        // Parse — JSON-mode or tool-call mode.
        TResponse? value;
        Exception? firstParseError;
        if (isToolCall)
        {
            (value, raw, firstParseError) = TryParseToolCall(response);
        }
        else
        {
            (value, raw, firstParseError) = TryParseJson(raw);
        }

        if (firstParseError is null)
        {
            return new LlmCallResult<TResponse>(value!, totalTokens, raw, ParseRetries: 0);
        }

        _logger.LogWarning(firstParseError,
            "LlmCall[{Name}] first-attempt parse failed; retrying with reminder. raw='{Raw}'",
            _descriptor.Name, Truncate(raw));

        // Retry once with the reminder appended.
        var reminder = isToolCall ? ToolCallRetryReminder : ParseRetryReminder;
        var userWithReminder = userMessage + "\n\n" + reminder;
        var (retryResponse, retryRaw) = await CallAsync(userWithReminder, includeRetryReminder: true, cancellationToken).ConfigureAwait(false);
        // Accumulate token usage if both calls surfaced it.
        var retryTokens = retryResponse.Usage?.TotalTokenCount;
        totalTokens = (totalTokens, retryTokens) switch
        {
            (null, null) => null,
            (long a, null) => a,
            (null, long b) => b,
            (long a, long b) => a + b
        };

        TResponse? retryValue;
        Exception? secondParseError;
        if (isToolCall)
        {
            (retryValue, retryRaw, secondParseError) = TryParseToolCall(retryResponse);
        }
        else
        {
            (retryValue, retryRaw, secondParseError) = TryParseJson(retryRaw);
        }

        if (secondParseError is null)
        {
            _logger.LogInformation(
                "LlmCall[{Name}] retry parse succeeded. retries=1",
                _descriptor.Name);
            return new LlmCallResult<TResponse>(retryValue!, totalTokens, retryRaw, ParseRetries: 1);
        }

        _logger.LogError(secondParseError,
            "LlmCall[{Name}] parse failed after 1 retry; surfacing LlmCallException. raw='{Raw}'",
            _descriptor.Name, Truncate(retryRaw));

        throw new LlmCallException(
            $"LlmCall[{_descriptor.Name}] failed to parse model response after 1 retry: {secondParseError.Message}",
            retryRaw,
            attempts: 2,
            descriptorName: _descriptor.Name,
            innerException: secondParseError);
    }

    // One call to the model. Returns (response, rawText). For tool-call mode the
    // raw text is the concatenated text content (usually empty), but callers
    // re-derive rawResponse from the function-call args.
    private async ValueTask<(ChatResponse Response, string Raw)> CallAsync(
        string userMessage,
        bool includeRetryReminder,
        CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, _descriptor.SystemPrompt),
            new(ChatRole.User, userMessage)
        };

        var chatOptions = new ChatOptions
        {
            ModelId = _descriptor.Model,
            Temperature = _descriptor.Temperature,
            MaxOutputTokens = _descriptor.MaxResponseTokens,
        };

        if (_descriptor.Tools is { Count: > 0 } tools)
        {
            // Tool-call mode: do NOT set ResponseFormat — providers
            // reject combining tools with json-mode.
            chatOptions.Tools = tools.Cast<AITool>().ToList();
        }
        else
        {
            chatOptions.ResponseFormat = _descriptor.ResponseFormat;
        }

        var response = await _chatClient.GetResponseAsync(messages, chatOptions, cancellationToken).ConfigureAwait(false);
        var raw = response.Text ?? string.Empty;
        return (response, raw);
    }

    // JSON-mode parse: strip fences, JsonDocument.Parse, dispatch to ParseResponse.
    // Single round-trip — the older shape went stripped → JsonNode → ToJsonString →
    // JsonDocument → Clone; that triple-trip was historic and unnecessary.
    private (TResponse? Value, string Raw, Exception? Error) TryParseJson(string raw)
    {
        var stripped = StripJsonFences(raw);
        if (string.IsNullOrWhiteSpace(stripped))
        {
            return (default, stripped, new InvalidOperationException("Empty response from model."));
        }

        JsonElement element;
        try
        {
            using var doc = JsonDocument.Parse(stripped);
            // Clone — the doc is disposed on exit; ParseResponse needs the
            // JsonElement to outlive the using block.
            element = doc.RootElement.Clone();
        }
        catch (JsonException jex)
        {
            return (default, stripped, jex);
        }

        try
        {
            var value = _descriptor.ParseResponse(element);
            return (value, stripped, null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            return (default, stripped, ex);
        }
    }

    // Tool-call mode parse: find first FunctionCallContent in the response
    // messages; pass (toolName, argumentsAsJsonElement) to ParseToolCall.
    private (TResponse? Value, string Raw, Exception? Error) TryParseToolCall(ChatResponse response)
    {
        FunctionCallContent? call = null;
        foreach (var message in response.Messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is FunctionCallContent fcc)
                {
                    call = fcc;
                    break;
                }
            }
            if (call is not null) break;
        }

        if (call is null)
        {
            var raw = response.Text ?? string.Empty;
            return (default, raw, new InvalidOperationException("Tool-call mode: model returned no FunctionCallContent."));
        }

        // Serialize the arguments dictionary to a JsonElement.
        JsonElement argsElement;
        string argsJson;
        try
        {
            var argsDict = call.Arguments ?? new Dictionary<string, object?>();
            argsJson = JsonSerializer.Serialize(argsDict, _descriptor.JsonOptions);
            using var doc = JsonDocument.Parse(argsJson);
            argsElement = doc.RootElement.Clone();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
        {
            return (default, string.Empty, ex);
        }

        try
        {
            // ParseToolCall is non-null in tool-call mode (constructor enforces).
            var value = _descriptor.ParseToolCall!(call.Name, argsElement);
            return (value, argsJson, null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            return (default, argsJson, ex);
        }
    }

    /// <summary>The canonical "strip ```json ... ``` or ``` ... ``` if
    /// the model wrapped its JSON" defence. Public-internal — the tests
    /// (and any consumer-authored adapter) can call this directly.</summary>
    internal static string StripJsonFences(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;

        // Drop the opening fence + optional language tag.
        var newlineIdx = trimmed.IndexOf('\n');
        if (newlineIdx < 0) return trimmed;
        var body = trimmed[(newlineIdx + 1)..];

        // Drop the trailing fence (preserving content if absent).
        var endIdx = body.LastIndexOf("```", StringComparison.Ordinal);
        if (endIdx > 0) body = body[..endIdx];

        return body.Trim();
    }

    private static string Truncate(string text, int max = 200)
        => text.Length <= max ? text : text[..max] + "…";
}
