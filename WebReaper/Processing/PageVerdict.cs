using WebReaper.Sinks.Models;

namespace WebReaper.Processing;

/// <summary>
/// What an <see cref="WebReaper.Processing.Abstract.IPageProcessor"/> decided
/// about one extracted target page (ADR-0038) — a closed sum with exactly two
/// arms (the ADR-0001 <c>CrawlOutcome</c> lineage): the record continues down
/// the pipeline, or the page is dropped. Construct only via <see cref="Keep"/>
/// / <see cref="Drop"/>; the hierarchy is not consumer-extensible.
/// </summary>
public abstract record PageVerdict
{
    private PageVerdict() { }

    /// <summary>
    /// The page proceeds carrying <see cref="Data"/> — the same record
    /// (observe), a mutated one (enrich), or a different one (replace /
    /// repair). The next processor sees it; once no processor is left, every
    /// Sink emits it.
    /// </summary>
    /// <param name="Data">The record to carry forward.</param>
    public sealed record Kept(ParsedData Data) : PageVerdict;

    /// <summary>
    /// The page is filtered out: no later processor runs, and no Sink emits it.
    /// </summary>
    /// <param name="Reason">Why the page was dropped — logged by the Crawl
    /// driver.</param>
    public sealed record Dropped(string Reason) : PageVerdict;

    /// <summary>Carry <paramref name="data"/> to the next stage — enrich,
    /// observe, or repair.</summary>
    /// <param name="data">The record to carry forward.</param>
    /// <returns>A <see cref="Kept"/> verdict.</returns>
    public static PageVerdict Keep(ParsedData data) => new Kept(data);

    /// <summary>Drop the page so no Sink emits it.</summary>
    /// <param name="reason">Why the page is dropped — logged by the Crawl
    /// driver.</param>
    /// <returns>A <see cref="Dropped"/> verdict.</returns>
    public static PageVerdict Drop(string reason) => new Dropped(reason);
}
