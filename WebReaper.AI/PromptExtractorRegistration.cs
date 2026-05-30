using Microsoft.Extensions.AI;
using WebReaper.AI.Llm;
using WebReaper.Builders;

namespace WebReaper.AI;

/// <summary>
/// The <c>ExtractWithPrompt</c> seed terminal (ADR-0084): schema-free LLM
/// extraction driven by a natural-language instruction. Composes on the
/// existing no-schema <c>AsMarkdown</c> terminal and overrides the content
/// extractor with a <see cref="PromptContentExtractor"/>, so it needs no new
/// core seam.
/// </summary>
public static class PromptExtractorRegistration
{
    /// <summary>Terminate the <see cref="ICrawlSeed"/> with the schema-free
    /// "Prompt extraction" strategy: the LLM extracts per
    /// <paramref name="instruction"/>, one call per page.</summary>
    /// <param name="seed">The crawl seed.</param>
    /// <param name="chatClient">The Microsoft.Extensions.AI chat client.</param>
    /// <param name="instruction">The natural-language extraction instruction.</param>
    /// <param name="options">Optional extractor options.</param>
    /// <returns>The configurable <see cref="ScraperEngineBuilder"/>.</returns>
    public static ScraperEngineBuilder ExtractWithPrompt(
        this ICrawlSeed seed,
        IChatClient chatClient,
        string instruction,
        LlmExtractorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);

        var builder = seed.AsMarkdown();
        var telemetry = builder.GetOrCreateLlmTelemetry();
        return builder.WithContentExtractor(
            new PromptContentExtractor(chatClient, instruction, options, telemetry));
    }
}
