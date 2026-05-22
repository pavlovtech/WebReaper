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
/// <param name="Document">The loaded page body, so the driver can build the
/// page-processor <c>PageContext</c> (ADR-0038, the <c>Html</c>).</param>
public sealed record JobReport(CrawlOutcome Outcome, string Document);
