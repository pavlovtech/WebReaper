using WebReaper.Core.Loaders.Abstract;

namespace WebReaper.Core.Crawling;

/// <summary>
/// The closed value the per-Job Spider shell returns (ADR-0022): the Crawl
/// step's <see cref="CrawlOutcome"/> plus the loaded document the Crawl driver
/// needs to build a page processor's <c>PageContext</c> (ADR-0038). The shell
/// <em>reports</em> what happened; the Crawl driver <em>decides</em> what to do
/// (run the page-processor pipeline, fan out to Sinks, de-duplicate and enqueue
/// child Jobs, mutate the Outstanding-work latch).
///
/// This <b>wraps</b> <see cref="CrawlOutcome"/> — it does not replace or extend
/// the ADR-0001 closed three-arm sum (that stays the Crawl step's result and
/// still lives inside the shell). Termination is never a thrown exception.
/// </summary>
/// <param name="Outcome">The Crawl step's closed result for this Job.</param>
/// <param name="Page">The loaded page (ADR-0083 <see cref="PageLoadResult"/>):
/// its <c>Html</c> builds the page-processor <c>PageContext</c> (ADR-0038), and
/// its status and headers carry the response metadata later slices read.</param>
public sealed record JobReport(CrawlOutcome Outcome, PageLoadResult Page);
