namespace WebReaper.AI.Llm;

/// <summary>
/// One LLM call's usage — produced by <see cref="LlmCall{TResponse}.InvokeAsync"/>
/// and reported to the registered <see cref="ILlmCallTelemetry"/> (ADR-0066).
/// Carries the per-call split (input / output / cached-input / total tokens)
/// plus the descriptor name (for per-adapter attribution — keyed by the
/// ADR-0059 <see cref="LlmCallDescriptor{TResponse}.Name"/>), parse retries,
/// and wall-clock duration.
/// </summary>
/// <param name="DescriptorName">The <see cref="LlmCallDescriptor{TResponse}.Name"/>
/// of the descriptor that produced the call — the per-adapter
/// attribution key.</param>
/// <param name="InputTokens">Mirrors
/// <see cref="LlmCallResult{TResponse}.InputTokens"/>.</param>
/// <param name="OutputTokens">Mirrors
/// <see cref="LlmCallResult{TResponse}.OutputTokens"/>.</param>
/// <param name="CachedInputTokens">Mirrors
/// <see cref="LlmCallResult{TResponse}.CachedInputTokens"/>.</param>
/// <param name="TotalTokens">Mirrors
/// <see cref="LlmCallResult{TResponse}.TotalTokens"/>.</param>
/// <param name="ParseRetries">0 when the first response parsed; 1 when the
/// bounded retry was needed. Reported on the parse-failure-after-retry path
/// as well (the <see cref="LlmCallException"/> exit) — operational signal
/// for "model is drifting from JSON / tool-call shape."</param>
/// <param name="Duration">Wall-clock time from <see cref="LlmCall{TResponse}.InvokeAsync"/>
/// entry to either return or exception throw. Useful for per-adapter
/// latency tracking.</param>
public sealed record LlmCallUsage(
    string DescriptorName,
    long? InputTokens,
    long? OutputTokens,
    long? CachedInputTokens,
    long? TotalTokens,
    int ParseRetries,
    TimeSpan Duration);
