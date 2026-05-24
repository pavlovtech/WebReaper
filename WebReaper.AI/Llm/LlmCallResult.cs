namespace WebReaper.AI.Llm;

/// <summary>
/// Structured return from <see cref="LlmCall{TResponse}.InvokeAsync"/>
/// (ADR-0059, extended by ADR-0065). The mechanism only ever produces this
/// for *successful* invocations — a parse-after-retry failure surfaces as
/// <see cref="LlmCallException"/> instead. The result carries the parsed
/// value plus the per-call observability fields: input / output /
/// cached-input / total token counts (ADR-0065 — was a single
/// <c>TotalTokens</c> field), raw response, and retry count.
/// </summary>
/// <param name="Value">The parsed domain type.</param>
/// <param name="InputTokens">Input tokens for this single call as
/// reported by
/// <c>Microsoft.Extensions.AI.ChatResponse.Usage.InputTokenCount</c>;
/// <c>null</c> when the chat client / model doesn't surface it. Per
/// Anthropic and OpenAI conventions, this is *inclusive* of cached reads
/// (the full prefix sent on the wire); <see cref="CachedInputTokens"/> is
/// the sub-count that was a cache read.</param>
/// <param name="OutputTokens">Output tokens for this single call as
/// reported by
/// <c>Microsoft.Extensions.AI.ChatResponse.Usage.OutputTokenCount</c>;
/// <c>null</c> when not surfaced.</param>
/// <param name="CachedInputTokens">Subset of <see cref="InputTokens"/>
/// served from provider-side prompt cache (ADR-0065). Read from
/// <c>UsageDetails.AdditionalCounts</c> via the provider-specific key
/// (<c>cached_input_tokens</c>, <c>cache_read_input_tokens</c>,
/// <c>prompt_tokens_details.cached_tokens</c>, …). <c>null</c> when the
/// provider does not surface cache details.</param>
/// <param name="TotalTokens">Total tokens for this single call. Equals
/// <see cref="InputTokens"/> + <see cref="OutputTokens"/> when both are
/// non-null; otherwise falls back to
/// <c>UsageDetails.TotalTokenCount</c>; otherwise <c>null</c>.
/// ADR-0051's <c>MaxBudgetTokens</c> reads this field directly via the
/// engine's accumulator (ADR-0066).</param>
/// <param name="RawResponse">The unparsed text (JSON-mode) or the
/// serialised tool-arguments JSON (tool-call mode). Useful for
/// debugging / logging.</param>
/// <param name="ParseRetries">0 when the first response parsed; 1 when
/// the bounded retry was needed.</param>
public sealed record LlmCallResult<TResponse>(
    TResponse Value,
    long? InputTokens,
    long? OutputTokens,
    long? CachedInputTokens,
    long? TotalTokens,
    string RawResponse,
    int ParseRetries);
