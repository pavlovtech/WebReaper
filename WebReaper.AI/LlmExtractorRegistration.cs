using Microsoft.Extensions.AI;
using WebReaper.AI.Llm;
using WebReaper.Builders;

namespace WebReaper.AI;

/// <summary>
/// The satellite's registration extensions (ADR-0009 pattern). Wires
/// <see cref="LlmContentExtractor"/> through the core's existing
/// <c>WithContentExtractor</c> seam (ADR-0039) so the LLM extractor
/// composes with every other registration without core changes.
/// </summary>
public static class LlmExtractorRegistration
{
    /// <summary>
    /// Use an LLM (via the supplied <paramref name="chatClient"/>) as
    /// the content extractor. The model is asked to return JSON matching
    /// the <see cref="WebReaper.Domain.Parsing.Schema"/> declared by
    /// <c>ICrawlSeed.Extract(schema)</c>. Defaults to Markdown
    /// pre-clean for ~10× token savings; pass
    /// <see cref="LlmExtractorOptions.UseMarkdownPreClean"/>=<c>false</c>
    /// to send raw HTML instead.
    /// </summary>
    public static ScraperEngineBuilder WithLlmExtractor(
        this ScraperEngineBuilder builder,
        IChatClient chatClient,
        LlmExtractorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(chatClient);
        var telemetry = builder.GetOrCreateLlmTelemetry();
        return builder.WithContentExtractor(new LlmContentExtractor(chatClient, options, telemetry));
    }

    /// <summary>
    /// Compose the currently-registered (or default) deterministic
    /// extractor with an LLM fallback (ADR-0046 routing + ADR-0044 LLM
    /// extractor). Run the deterministic fold first; on validation
    /// failure (default: any required schema leaf empty or absent),
    /// escalate to the LLM. The deterministic-first → LLM-fallback
    /// wedge in one method.
    /// </summary>
    public static ScraperEngineBuilder WithLlmFallback(
        this ScraperEngineBuilder builder,
        IChatClient chatClient,
        LlmExtractorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(chatClient);
        var telemetry = builder.GetOrCreateLlmTelemetry();
        return builder.WithFallbackExtractor(new LlmContentExtractor(chatClient, options, telemetry));
    }

    /// <summary>
    /// Wrap the current/default deterministic extractor with the
    /// LLM-driven self-healing wrapper (ADR-0047): on a failed
    /// deterministic pass, ask the LLM for patched selectors,
    /// validate by re-running the fold, and cache the patch for
    /// every subsequent page of the Crawl. The LLM-as-proposer /
    /// fold-as-validator wedge in one method.
    /// </summary>
    public static ScraperEngineBuilder WithLlmSelfHealing(
        this ScraperEngineBuilder builder,
        IChatClient chatClient,
        LlmExtractorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(chatClient);
        var telemetry = builder.GetOrCreateLlmTelemetry();
        return builder.WithSelfHealing(new LlmSelectorRepairer(chatClient, options, telemetry));
    }
}
