using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private readonly ILlmCallTelemetry _telemetry;
    private readonly ILogger _logger;

    /// <summary>Construct an <see cref="LlmCall{TResponse}"/>.</summary>
    /// <param name="chatClient">The Microsoft.Extensions.AI chat client
    /// (ADR-0009 quarantine — the consumer brings their own concrete
    /// implementation: OpenAI, Anthropic via wrapper, Ollama, …).</param>
    /// <param name="descriptor">The per-role policy record.</param>
    /// <param name="telemetry">Optional telemetry accumulator (ADR-0066).
    /// When omitted, <see cref="NullLlmCallTelemetry.Instance"/> is used —
    /// successful and failed-after-retry calls are discarded. The four
    /// built-in adapters thread an instance from the builder-side
    /// <c>LlmTelemetry</c> handle when wired via the <c>WithLlm*</c>
    /// extensions; consumer-authored adapters constructed à la carte
    /// default to the null implementation.</param>
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
        ILlmCallTelemetry? telemetry = null,
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
        _telemetry = telemetry ?? NullLlmCallTelemetry.Instance;
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
        // ADR-0066: wall-clock duration for the telemetry record (one
        // measurement covering both first-attempt and retry, so the
        // accumulator's TotalDuration matches the InvokeAsync entry-to-exit
        // time and not just the chat-client portion).
        var sw = Stopwatch.StartNew();

        // First attempt.
        var (response, raw) = await CallAsync(userMessage, includeRetryReminder: false, cancellationToken).ConfigureAwait(false);
        // ADR-0065: capture the split usage (input / output / cached /
        // total) — was a single TotalTokenCount read pre-0065.
        var usage = ReadUsage(response.Usage);

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
            sw.Stop();
            _telemetry.Record(new LlmCallUsage(
                DescriptorName: _descriptor.Name,
                InputTokens: usage.Input,
                OutputTokens: usage.Output,
                CachedInputTokens: usage.Cached,
                TotalTokens: usage.Total,
                ParseRetries: 0,
                Duration: sw.Elapsed));
            return new LlmCallResult<TResponse>(
                value!,
                InputTokens: usage.Input,
                OutputTokens: usage.Output,
                CachedInputTokens: usage.Cached,
                TotalTokens: usage.Total,
                raw,
                ParseRetries: 0);
        }

        _logger.LogWarning(firstParseError,
            "LlmCall[{Name}] first-attempt parse failed; retrying with reminder. raw='{Raw}'",
            _descriptor.Name, Truncate(raw));

        // Retry once with the reminder appended.
        var reminder = isToolCall ? ToolCallRetryReminder : ParseRetryReminder;
        var userWithReminder = userMessage + "\n\n" + reminder;
        var (retryResponse, retryRaw) = await CallAsync(userWithReminder, includeRetryReminder: true, cancellationToken).ConfigureAwait(false);
        // ADR-0065: accumulate across both calls — input / output / cached
        // / total summed independently with null-respecting semantics
        // (matches the pre-0065 TotalTokens-only accumulator). M.E.AI's
        // UsageDetails.AdditionalCounts is documented "all values set
        // here are assumed to be summable" — explicitly endorses this
        // pattern.
        usage = AccumulateUsage(usage, ReadUsage(retryResponse.Usage));

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
            sw.Stop();
            _logger.LogInformation(
                "LlmCall[{Name}] retry parse succeeded. retries=1",
                _descriptor.Name);
            _telemetry.Record(new LlmCallUsage(
                DescriptorName: _descriptor.Name,
                InputTokens: usage.Input,
                OutputTokens: usage.Output,
                CachedInputTokens: usage.Cached,
                TotalTokens: usage.Total,
                ParseRetries: 1,
                Duration: sw.Elapsed));
            return new LlmCallResult<TResponse>(
                retryValue!,
                InputTokens: usage.Input,
                OutputTokens: usage.Output,
                CachedInputTokens: usage.Cached,
                TotalTokens: usage.Total,
                retryRaw,
                ParseRetries: 1);
        }

        sw.Stop();
        _logger.LogError(secondParseError,
            "LlmCall[{Name}] parse failed after 1 retry; surfacing LlmCallException. raw='{Raw}'",
            _descriptor.Name, Truncate(retryRaw));
        // ADR-0066: report the failed-after-retry call too — telemetry
        // counts both successful and exhausted calls for accurate
        // run-cost accounting.
        _telemetry.Record(new LlmCallUsage(
            DescriptorName: _descriptor.Name,
            InputTokens: usage.Input,
            OutputTokens: usage.Output,
            CachedInputTokens: usage.Cached,
            TotalTokens: usage.Total,
            ParseRetries: 1,
            Duration: sw.Elapsed));

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
        // ADR-0065: optional cache_control hint on the system message.
        // Anthropic-standard encoding via AdditionalProperties (M.E.AI
        // 9.4-preview documents AdditionalProperties as the "any
        // additional properties associated with the message" channel —
        // verified at design time). OpenAI / Gemini / local-model
        // adapters ignore the unknown key without error; the descriptor
        // surface is stable regardless of where the encoding lands at
        // the wire.
        var systemMessage = new ChatMessage(ChatRole.System, _descriptor.SystemPrompt);
        if (_descriptor.SystemPromptCache == CachePolicy.Hinted)
        {
            systemMessage.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            systemMessage.AdditionalProperties["cache_control"]
                = new Dictionary<string, object?> { ["type"] = "ephemeral" };
        }

        var messages = new List<ChatMessage>
        {
            systemMessage,
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
            // ADR-0084: materialise the arguments dictionary to JSON without
            // reflection. The JsonSerializer.Serialize<IDictionary<string,object>>
            // overload is AOT-hostile; building a JsonObject via the implicit
            // node conversions and ToJsonString is not. Argument values are
            // JsonElement in practice (what a chat client surfaces).
            var argsObj = new JsonObject();
            foreach (var kvp in argsDict)
                argsObj[kvp.Key] = ArgToNode(kvp.Value);
            argsJson = argsObj.ToJsonString();
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

    // ADR-0084: reflection-free object -> JsonNode for tool-call argument
    // values, behaviour-equivalent to the prior JsonSerializer.Serialize for
    // the shapes tool calls actually carry. The realistic value is JsonElement
    // (what a chat client surfaces); hand-built argument dictionaries nest
    // IDictionary / IEnumerable / primitives, which recurse here. Primitives
    // use the implicit JsonNode conversions (AOT-safe, the same path the
    // JsonObject indexer uses); anything exotic is stringified rather than
    // reaching for reflection. Order matters: string before IEnumerable (a
    // string is enumerable), IDictionary before IEnumerable (a dictionary is
    // enumerable), JsonNode before both (JsonObject/JsonArray are enumerable).
    private static JsonNode? ArgToNode(object? value) => value switch
    {
        null => null,
        JsonElement je => JsonNode.Parse(je.GetRawText()),
        JsonNode node => node.DeepClone(),
        bool b => (JsonNode?)b,
        int i => (JsonNode?)i,
        long l => (JsonNode?)l,
        double d => (JsonNode?)d,
        decimal m => (JsonNode?)m,
        string s => (JsonNode?)s,
        IDictionary<string, object?> dict => DictToObject(dict),
        System.Collections.IEnumerable seq => SeqToArray(seq),
        _ => (JsonNode?)(value.ToString() ?? string.Empty)
    };

    private static JsonObject DictToObject(IDictionary<string, object?> dict)
    {
        var obj = new JsonObject();
        foreach (var kvp in dict)
            obj[kvp.Key] = ArgToNode(kvp.Value);
        return obj;
    }

    private static JsonArray SeqToArray(System.Collections.IEnumerable seq)
    {
        var array = new JsonArray();
        foreach (var item in seq)
            array.Add(ArgToNode(item));
        return array;
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

    // ADR-0065: read UsageDetails into the per-call split tuple
    // (input / output / cached / total). CachedInputTokens scans
    // AdditionalCounts for the provider-specific key — M.E.AI 9.4
    // doesn't normalise across providers, so the mechanism tries the
    // conventional names. Null when no key matches.
    private static (long? Input, long? Output, long? Cached, long? Total) ReadUsage(UsageDetails? usage)
    {
        if (usage is null) return (null, null, null, null);
        var input = usage.InputTokenCount;
        var output = usage.OutputTokenCount;
        long? cached = null;
        if (usage.AdditionalCounts is { } ac)
        {
            // Try the conventional cache-read key names across providers.
            // Anthropic: "cache_read_input_tokens"; OpenAI: typically
            // surfaces "InputTokenCount.Cached" via the M.E.AI OpenAI
            // adapter; the generic "cached_input_tokens" and
            // "prompt_tokens_details.cached_tokens" round out the
            // recognised set. Unknown providers / adapters: null.
            if (ac.TryGetValue("cached_input_tokens", out var c1)) cached = c1;
            else if (ac.TryGetValue("InputTokenCount.Cached", out var c2)) cached = c2;
            else if (ac.TryGetValue("prompt_tokens_details.cached_tokens", out var c3)) cached = c3;
            else if (ac.TryGetValue("cache_read_input_tokens", out var c4)) cached = c4;
        }
        var total = (input, output) switch
        {
            (long i, long o) => (long?)(i + o),
            _ => usage.TotalTokenCount
        };
        return (input, output, cached, total);
    }

    // ADR-0065: per-field null-respecting accumulator (matches the
    // pre-0065 totalTokens-only accumulator's semantics — applied
    // independently to each of input / output / cached / total).
    private static (long? Input, long? Output, long? Cached, long? Total) AccumulateUsage(
        (long? Input, long? Output, long? Cached, long? Total) a,
        (long? Input, long? Output, long? Cached, long? Total) b)
        => (Input: SumNullable(a.Input, b.Input),
            Output: SumNullable(a.Output, b.Output),
            Cached: SumNullable(a.Cached, b.Cached),
            Total: SumNullable(a.Total, b.Total));

    private static long? SumNullable(long? a, long? b) => (a, b) switch
    {
        (null, null) => null,
        (long x, null) => x,
        (null, long x) => x,
        (long x, long y) => x + y
    };
}
