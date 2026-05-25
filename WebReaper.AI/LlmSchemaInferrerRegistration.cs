using Microsoft.Extensions.AI;
using WebReaper.AI.Llm;
using WebReaper.Builders;

namespace WebReaper.AI;

/// <summary>
/// The satellite's <see cref="LlmSchemaInferrer"/> registration extension
/// (ADR-0009 pattern). Wires the LLM-backed
/// <see cref="WebReaper.Core.Parser.Abstract.ISchemaInferrer"/> through the
/// core's <see cref="ScraperEngineBuilder.WithSchemaInferrer"/> seam
/// (ADR-0067) so the inferrer composes with every other registration
/// without core changes.
/// </summary>
public static class LlmSchemaInferrerRegistration
{
    /// <summary>
    /// Register an LLM-backed schema inferrer (ADR-0067): the
    /// <see cref="WebReaper.Core.Parser.Concrete.LearnedSchemaContentExtractor"/>
    /// wrapper invokes it on the first page of a crawl that chose the
    /// <see cref="WebReaper.Builders.ICrawlSeed.ExtractInferred(string?)"/>
    /// seed terminal; the proposed <see cref="WebReaper.Domain.Parsing.Schema"/>
    /// is cached on the wrapper and consumed by the deterministic fold
    /// for every subsequent page. First page pays the LLM; every
    /// subsequent page runs the cheap path.
    /// <para>
    /// Threads the per-builder <see cref="ILlmCallTelemetry"/> handle
    /// (ADR-0066) so the inferred-schema call shows up in the
    /// per-run <c>RunReport.Llm</c> snapshot under
    /// <c>nameof(LlmSchemaInferrer)</c> alongside the other adapters.
    /// </para>
    /// <para>
    /// Silently no-ops on the runtime path when the consumer chose
    /// <see cref="WebReaper.Builders.ICrawlSeed.Extract"/> or
    /// <see cref="WebReaper.Builders.ICrawlSeed.AsMarkdown"/> instead —
    /// <see cref="WebReaper.Builders.ScraperEngineBuilder.BuildAsync"/>
    /// only composes the <c>LearnedSchemaContentExtractor</c> wrapper
    /// when <c>ExtractInferred</c> was called.
    /// </para>
    /// </summary>
    /// <param name="builder">The scraper builder.</param>
    /// <param name="chatClient">The Microsoft.Extensions.AI chat client.</param>
    /// <param name="options">Optional <see cref="LlmSchemaInferrerOptions"/>;
    /// defaults applied when null.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> or <paramref name="chatClient"/> is
    /// null.</exception>
    public static ScraperEngineBuilder WithLlmSchemaInferrer(
        this ScraperEngineBuilder builder,
        IChatClient chatClient,
        LlmSchemaInferrerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(chatClient);
        var opts = options ?? new LlmSchemaInferrerOptions();
        var telemetry = builder.GetOrCreateLlmTelemetry();
        // ADR-0069: thread the satellite's per-role re-inference triggers
        // through to the wrapper at BuildAsync time. The satellite's
        // defaults flip the core wrapper's behaviour from
        // "trust-the-cache" to "re-infer after 3 consecutive validation
        // failures" — the headline ADR-0069 opt-out shape. Consumers
        // can call WithSchemaInferenceTriggers directly afterwards to
        // override.
        builder.WithSchemaInferenceTriggers(
            opts.ReInferAfterFailures,
            opts.MaxReInferencesPerInstance);
        return builder.WithSchemaInferrer(new LlmSchemaInferrer(chatClient, opts, telemetry));
    }
}
