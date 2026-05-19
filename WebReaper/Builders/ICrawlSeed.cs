using WebReaper.Domain;
using WebReaper.Domain.Parsing;

namespace WebReaper.Builders;

/// <summary>
/// A <b>Crawl seed</b> (ADR-0025): the start URL(s) and page mode produced by
/// <see cref="ScraperEngineBuilder.Crawl(string[])"/> /
/// <see cref="ScraperEngineBuilder.CrawlWithBrowser(string[])"/>, awaiting a
/// <see cref="Schema"/>. It is not yet a builder — its only operation is
/// <see cref="Extract"/>. This is what makes "build with no start URLs or no
/// schema" unrepresentable: the gated terminals
/// (<see cref="ScraperEngineBuilder.BuildAsync"/>,
/// <see cref="ScraperEngineBuilder.Build"/>) live only on the
/// <see cref="ScraperEngineBuilder"/> that <see cref="Extract"/> returns, and
/// that builder has an internal constructor — it cannot exist without a seed
/// and a schema.
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
}
