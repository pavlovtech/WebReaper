namespace WebReaper.AI.Llm;

/// <summary>
/// Structured return from <see cref="LlmCall{TResponse}.InvokeAsync"/>
/// (ADR-0059). The mechanism only ever produces this for *successful*
/// invocations — a parse-after-retry failure surfaces as
/// <see cref="LlmCallException"/> instead. The result carries the parsed
/// value plus the observability fields ADR-0051's <c>MaxBudgetTokens</c>
/// (token usage) and debugging callers (raw response, retry count) read.
/// </summary>
/// <param name="Value">The parsed domain type.</param>
/// <param name="TotalTokens">Total tokens for this single call as
/// reported by <c>ChatResponse.Usage.TotalTokenCount</c>; <c>null</c>
/// when the chat client / model doesn't surface usage.</param>
/// <param name="RawResponse">The unparsed text (JSON-mode) or the
/// serialised tool-arguments JSON (tool-call mode). Useful for
/// debugging / logging.</param>
/// <param name="ParseRetries">0 when the first response parsed; 1 when
/// the bounded retry was needed.</param>
public sealed record LlmCallResult<TResponse>(
    TResponse Value,
    long? TotalTokens,
    string RawResponse,
    int ParseRetries);
