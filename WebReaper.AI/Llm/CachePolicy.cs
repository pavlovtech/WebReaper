namespace WebReaper.AI.Llm;

/// <summary>
/// Per-role policy for system-prompt caching (ADR-0065). The mechanism
/// (<see cref="LlmCall{TResponse}"/>) encodes the policy as a
/// provider-specific hint on the outbound system <see cref="Microsoft.Extensions.AI.ChatMessage"/>;
/// providers that recognise the hint cache the prefix, providers that
/// do not silently ignore it.
/// </summary>
/// <remarks>
/// <para>
/// Default in <see cref="WebReaper.AI.AiOptions"/> is
/// <see cref="Hinted"/> — the AI-native one-line
/// <see cref="WebReaper.AI.UseAiRegistration.UseAi(WebReaper.Builders.ScraperEngineBuilder, Microsoft.Extensions.AI.IChatClient, WebReaper.AI.AiOptions?)"/>
/// enables caching by default. Anthropic users benefit
/// (<c>cache_control</c> attaches to the system message); OpenAI users
/// see no change (auto-cache continues regardless); Gemini / local-model
/// users see the unknown property ignored without error.
/// </para>
/// <para>
/// Single-page scrapes pay a ~25% cache-write premium with no second
/// hit to amortise — override per-role to <see cref="Default"/> for
/// one-shot consumers, or set
/// <c>AiOptions(CachePolicy: CachePolicy.Default)</c> globally.
/// </para>
/// </remarks>
public enum CachePolicy
{
    /// <summary>
    /// The mechanism does NOT add a provider-specific cache hint.
    /// Providers that auto-cache (OpenAI — stable prefix ≥ 1024 tokens)
    /// still cache; providers that need explicit hints (Anthropic) do
    /// not. <see cref="LlmCallResult{TResponse}.CachedInputTokens"/> is
    /// still populated when the provider surfaces it.
    /// </summary>
    Default,

    /// <summary>
    /// The mechanism adds <c>cache_control: { type: "ephemeral" }</c>
    /// to the system <see cref="Microsoft.Extensions.AI.ChatMessage.AdditionalProperties"/>.
    /// Anthropic interprets this as a 5-minute ephemeral cache marker;
    /// OpenAI ignores the hint (auto-caching is unchanged); other
    /// providers ignore the hint without error. The wire-level
    /// translation depends on the consumer's chosen
    /// <see cref="Microsoft.Extensions.AI.IChatClient"/> adapter.
    /// </summary>
    Hinted
}
