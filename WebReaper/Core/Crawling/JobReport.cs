namespace WebReaper.Core.Crawling;

/// <summary>
/// The closed value the per-Job Spider shell returns (ADR-0022): the Crawl
/// step's <see cref="CrawlOutcome"/> plus the loaded document the Crawl driver
/// needs to build the PostProcessor <c>Metadata</c>. The shell <em>reports</em>
/// what happened; the Crawl driver <em>decides</em> what to do (fan out to
/// Sinks, fire callbacks, de-duplicate and enqueue child Jobs, mutate the
/// Outstanding-work latch).
///
/// This <b>wraps</b> <see cref="CrawlOutcome"/> — it does not replace or extend
/// the ADR-0001 closed three-arm sum (that stays the Crawl step's result and
/// still lives inside the shell). Termination is never a thrown exception.
/// </summary>
public sealed record JobReport(CrawlOutcome Outcome, string Document);
