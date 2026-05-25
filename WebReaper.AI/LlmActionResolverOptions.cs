using WebReaper.AI.Llm;

namespace WebReaper.AI;

/// <summary>
/// Knobs for <see cref="LlmActionResolver"/> (ADR-0050). Defaults are
/// cheap and deterministic: temperature 0, a 512-token response cap
/// (the JSON object is tiny), and an 8192-token cap on the HTML the
/// prompt carries (trimmed from the end if longer), no cache-policy
/// override.
/// </summary>
/// <param name="Model">The model id passed to
/// <c>ChatOptions.ModelId</c>. <c>null</c> means the chat client's
/// default — most consumers configure the model at the
/// <c>IChatClient</c> level and leave this null.</param>
/// <param name="Temperature">Sampling temperature. Default 0 — action
/// resolution is a deterministic task, the same intent should resolve
/// to the same selector across pages with the same DOM.</param>
/// <param name="MaxResponseTokens">Response token cap
/// (<c>ChatOptions.MaxOutputTokens</c>). Default 512 — the resolver
/// returns a small JSON object naming one action arm.</param>
/// <param name="MaxHtmlChars">Maximum character count of the page HTML
/// the prompt embeds (truncated from the end if longer). Default 32_000
/// — roughly 8k tokens at the typical 4-chars-per-token rate. A
/// character cap rather than a tokeniser-aware budget is deliberate:
/// keeps the satellite zero-dependency beyond
/// <c>Microsoft.Extensions.AI.Abstractions</c>. Lower for cost,
/// raise for pages whose action-relevant chrome lives below the
/// fold.</param>
/// <param name="SystemPrompt">Override the default action-resolution
/// system prompt. <c>null</c> uses the built-in prompt; supply a string
/// to override entirely.</param>
/// <param name="CachePolicy">Per-role system-prompt caching policy
/// (ADR-0065). <c>null</c> (default) inherits from
/// <see cref="AiOptions.CachePolicy"/> via
/// <see cref="UseAiRegistration.UseAi(WebReaper.Builders.ScraperEngineBuilder, Microsoft.Extensions.AI.IChatClient, AiOptions?)"/>;
/// see <see cref="LlmExtractorOptions.CachePolicy"/> for the full
/// inheritance / à-la-carte semantics.</param>
public sealed record LlmActionResolverOptions(
    string? Model = null,
    float Temperature = 0.0f,
    int MaxResponseTokens = 512,
    int MaxHtmlChars = 32_000,
    string? SystemPrompt = null,
    CachePolicy? CachePolicy = null);
