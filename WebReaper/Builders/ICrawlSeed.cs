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

    /// <summary>
    /// Choose runtime schema inference (ADR-0067): no <see cref="Schema"/>
    /// supplied at build time; the registered
    /// <see cref="WebReaper.Core.Parser.Abstract.ISchemaInferrer"/> proposes
    /// one from the first page's content, the deterministic fold consumes it
    /// for every subsequent page. The LLM-as-proposer /
    /// deterministic-as-validator wedge applied to schema generation —
    /// the firecrawl-shaped "extract structured data without a schema"
    /// path; the fifth dock of the project-level pattern (sibling to
    /// ADR-0046 routing, ADR-0047 selector repair, ADR-0050 action
    /// resolution, ADR-0051 page selection).
    /// <para>
    /// Requires an
    /// <see cref="WebReaper.Core.Parser.Abstract.ISchemaInferrer"/>
    /// registered via
    /// <see cref="ScraperEngineBuilder.WithSchemaInferrer"/>
    /// or the satellite's
    /// <c>WithLlmSchemaInferrer(IChatClient, LlmSchemaInferrerOptions?)</c>
    /// extension before <see cref="ScraperEngineBuilder.BuildAsync"/>; the
    /// build throws <see cref="System.InvalidOperationException"/> otherwise.
    /// </para>
    /// <para>
    /// First page pays the inferrer (one LLM call); every subsequent page
    /// runs the deterministic fold against the cached schema. The cache
    /// lives on the wrapper instance — fresh engine = fresh inference.
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// var engine = await ScraperEngineBuilder
    ///     .Crawl("https://shop.com/products")
    ///     .ExtractInferred(goal: "product details")
    ///     .WithLlmSchemaInferrer(chatClient)
    ///     .WriteToConsole()
    ///     .BuildAsync();
    /// </code>
    /// </para>
    /// </summary>
    /// <param name="goal">Optional natural-language hint about what to
    /// extract (<c>"product details"</c>, <c>"job listings"</c>, …).
    /// Threaded through to the inferrer; when null the inferrer guesses
    /// from the page content.</param>
    ScraperEngineBuilder ExtractInferred(string? goal = null);
}
