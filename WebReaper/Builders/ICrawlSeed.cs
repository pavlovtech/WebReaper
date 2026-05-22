using WebReaper.Domain;
using WebReaper.Domain.Parsing;

namespace WebReaper.Builders;

/// <summary>
/// A <b>Crawl seed</b> (ADR-0025): the start URL(s) and page mode produced by
/// <see cref="ScraperEngineBuilder.Crawl(string[])"/> /
/// <see cref="ScraperEngineBuilder.CrawlWithBrowser(string[])"/>, awaiting an
/// <em>extraction strategy</em>. It is not yet a builder — its only operations
/// are the closed lattice of strategy terminals: <see cref="Extract"/>
/// (Schema-driven structured extraction, ADR-0002 deterministic fold) and
/// <see cref="AsMarkdown"/> (no-schema LLM-ready Markdown extraction,
/// ADR-0040). This is what makes "build with no start URLs or no extraction
/// strategy" unrepresentable: the gated terminals
/// (<see cref="ScraperEngineBuilder.BuildAsync"/>,
/// <see cref="ScraperEngineBuilder.Build"/>) live only on the
/// <see cref="ScraperEngineBuilder"/> these methods return, and that builder
/// has an internal constructor — it cannot exist without a seed and a chosen
/// strategy.
/// </summary>
public interface ICrawlSeed
{
    /// <summary>
    /// Apply the extraction <see cref="Schema"/> (the shared fold grammar,
    /// ADR-0002) and yield the configurable <see cref="ScraperEngineBuilder"/>
    /// — every free fluent method and every satellite extension, terminating
    /// in <see cref="ScraperEngineBuilder.BuildAsync"/> (a runnable engine) or
    /// <see cref="ScraperEngineBuilder.Build"/> (just the
    /// <see cref="ScraperConfig"/>).
    /// </summary>
    ScraperEngineBuilder Extract(Schema schema);

    /// <summary>
    /// Choose Markdown extraction (ADR-0040): no <see cref="Schema"/>,
    /// LLM-ready Markdown of each target page's main content. Yields the
    /// configurable <see cref="ScraperEngineBuilder"/> — every free fluent
    /// method and every satellite extension. The funnel's no-schema wedge:
    /// <code>
    /// var engine = await ScraperEngineBuilder
    ///     .Crawl("https://example.com")
    ///     .AsMarkdown()
    ///     .WriteToConsole()
    ///     .BuildAsync();
    /// await engine.RunAsync();
    /// </code>
    /// Emitted <see cref="WebReaper.Sinks.Models.ParsedData"/> carries
    /// <c>title</c> and <c>markdown</c> fields (plus ADR-0031's <c>url</c>).
    /// Deterministic and AOT-clean — no LLM dependency; the
    /// "AI-native" feel is the output format, not an inference call.
    /// </summary>
    ScraperEngineBuilder AsMarkdown();
}
