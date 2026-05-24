using System.Text.Json;
using Microsoft.Extensions.AI;

namespace WebReaper.AI.Llm;

/// <summary>
/// Per-role policy record for <see cref="LlmCall{TResponse}"/> (ADR-0059).
/// The descriptor carries the four pieces that vary across the four
/// <c>WebReaper.AI</c> LLM adapters — the system prompt, the per-call
/// user-message build, the response-shape parse, and (for ADR-0060
/// tool-calling adapters) the tool list + tool-call parse.
/// <para>
/// Composition over inheritance: the mechanism (<see cref="LlmCall{TResponse}"/>)
/// owns the transport, code-fence stripping, bounded parse-retry, and
/// <see cref="ChatResponse.Usage"/> capture; the descriptor owns nothing
/// but data + delegates.
/// </para>
/// </summary>
/// <typeparam name="TResponse">The domain type the parsed response yields
/// (e.g. <see cref="System.Text.Json.Nodes.JsonObject"/> for the extractor,
/// <c>AgentDecision</c> for the brain).</typeparam>
public sealed record LlmCallDescriptor<TResponse>
{
    /// <summary>The role's invariant system prompt — pinned as the first
    /// <see cref="ChatMessage"/> of every <see cref="LlmCall{TResponse}.InvokeAsync"/>
    /// invocation.</summary>
    public required string SystemPrompt { get; init; }

    /// <summary>Build the per-call user message from the role's input.
    /// Pure function; called once per <see cref="LlmCall{TResponse}.InvokeAsync"/>,
    /// plus once more on a parse-retry (with the reminder appended).
    /// The input shape is the adapter's choice; the mechanism is
    /// type-erased here (<see cref="object"/>) to keep the descriptor
    /// generic on <typeparamref name="TResponse"/> only.</summary>
    public required Func<object, string> BuildUserMessage { get; init; }

    /// <summary>Parse the model's JSON response into the role's domain
    /// type. Receives the parsed <see cref="JsonElement"/> (post-fence-
    /// strip). Throws on unrepairable parse failure — the mechanism
    /// translates the throw into a one-shot retry and, on second
    /// failure, an <see cref="LlmCallException"/>.</summary>
    public required Func<JsonElement, TResponse> ParseResponse { get; init; }

    /// <summary>The <see cref="JsonSerializerOptions"/> the descriptor's
    /// <see cref="ParseResponse"/> / <see cref="ParseToolCall"/> delegates
    /// may consult. Default is <see cref="JsonSerializerOptions.Default"/> —
    /// the mechanism never consumes this field directly, it's passed
    /// to the parse delegates as a convenience.</summary>
    public JsonSerializerOptions JsonOptions { get; init; } = JsonSerializerOptions.Default;

    /// <summary>Optional model id override. Default <c>null</c> — the
    /// chat client's configured model wins.</summary>
    public string? Model { get; init; }

    /// <summary>Sampling temperature. Default <c>0</c> — extraction /
    /// resolution / decision are deterministic tasks.</summary>
    public float Temperature { get; init; } = 0.0f;

    /// <summary>Per-call response token cap (<see cref="ChatOptions.MaxOutputTokens"/>).
    /// Default <c>4096</c>.</summary>
    public int MaxResponseTokens { get; init; } = 4096;

    /// <summary>The chat response format. Default <see cref="ChatResponseFormat.Json"/> —
    /// the descriptor pattern keeps it overridable for the rare role
    /// (e.g. a future verifier returning a plain bool / number).</summary>
    public ChatResponseFormat ResponseFormat { get; init; } = ChatResponseFormat.Json;

    /// <summary>Optional tool list — the ADR-0060 seam. When non-null
    /// the mechanism switches from JSON-mode parsing to tool-call
    /// parsing: <see cref="ParseResponse"/> is bypassed and
    /// <see cref="ParseToolCall"/> is invoked instead. Items are
    /// <see cref="AIFunction"/> rather than <see cref="AITool"/> because
    /// every tool-call adapter ships function-shaped tools (the only
    /// concrete subtype as of M.E.AI 9.4 preview).</summary>
    public IReadOnlyList<AIFunction>? Tools { get; init; }

    /// <summary>Required when <see cref="Tools"/> is non-null. The
    /// mechanism finds the first <see cref="FunctionCallContent"/> in
    /// the response and passes <c>(toolName, argumentsJson)</c> to this
    /// delegate. ADR-0060 binds this in 0060.</summary>
    public Func<string, JsonElement, TResponse>? ParseToolCall { get; init; }

    /// <summary>Optional descriptor name surfaced in
    /// <see cref="LlmCallException"/> messages for diagnostics. Default
    /// <see cref="string.Empty"/>; adapters pass their type name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Per-role caching policy for the system prompt (ADR-0065).
    /// Default <see cref="CachePolicy.Default"/> — providers that auto-cache
    /// (OpenAI) still benefit; explicit-hint providers (Anthropic) do not.
    /// Set to <see cref="CachePolicy.Hinted"/> to add the provider-specific
    /// <c>cache_control</c> hint to the system <see cref="ChatMessage.AdditionalProperties"/>.
    /// The four built-in adapters read this from their per-role
    /// <c>*Options.CachePolicy</c>; <see cref="WebReaper.AI.UseAiRegistration.UseAi(WebReaper.Builders.ScraperEngineBuilder, Microsoft.Extensions.AI.IChatClient, WebReaper.AI.AiOptions?)"/>
    /// flows the global <see cref="WebReaper.AI.AiOptions.CachePolicy"/>
    /// through the <c>Resolve*</c> helpers when synthesising per-role
    /// records from global defaults.</summary>
    public CachePolicy SystemPromptCache { get; init; } = CachePolicy.Default;
}
