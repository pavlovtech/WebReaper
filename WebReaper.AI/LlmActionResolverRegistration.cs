using Microsoft.Extensions.AI;
using WebReaper.AI.Llm;
using WebReaper.Builders;

namespace WebReaper.AI;

/// <summary>
/// The satellite's <see cref="LlmActionResolver"/> registration extensions
/// (ADR-0009 pattern). Wires the LLM-backed
/// <see cref="WebReaper.Core.Actions.Abstract.IActionResolver"/> through the
/// core's <c>WithActionResolver</c> seam (ADR-0050) so the resolver composes
/// with every other registration without core changes.
/// </summary>
public static class LlmActionResolverRegistration
{
    /// <summary>
    /// Register an LLM-backed action resolver (ADR-0050): the Puppeteer
    /// transport invokes it on the first <c>SemanticAct(intent)</c> per crawl,
    /// caches the resolved <see cref="WebReaper.Domain.PageActions.PageAction"/>
    /// arm, and dispatches the cached arm on every subsequent same-intent
    /// page. The deterministic path is the hot path; the LLM is the
    /// fallback / repair.
    /// </summary>
    public static ScraperEngineBuilder WithLlmActionResolver(
        this ScraperEngineBuilder builder,
        IChatClient chatClient,
        LlmActionResolverOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(chatClient);
        var telemetry = builder.GetOrCreateLlmTelemetry();
        return builder.WithActionResolver(new LlmActionResolver(chatClient, options, telemetry));
    }
}
