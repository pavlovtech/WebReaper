namespace WebReaper.Core.Crawling.Concrete;

/// <summary>
/// The crawl-global on-domain + depth policy a Site sweep (ADR-0081) applies
/// when a <see cref="CrawlStep"/> produces a Sweep page's child Jobs. Threaded
/// into the step at construction from the
/// <see cref="WebReaper.Domain.ScraperConfig"/>. The
/// <see cref="AnchorHost"/> is derived from the start URL, so it is fixed for
/// the whole crawl (the start host, not the per-page host, which is what makes
/// <see cref="IncludeSubdomains"/> correct as pages are reached through
/// <c>www</c> and subdomains). Null on a non-sweep crawl, in which case the
/// step's recursive branch never fires; when null but a recursive selector is
/// somehow present the step falls back to the page's own host, no subdomains,
/// and unbounded depth.
/// </summary>
/// <param name="AnchorHost">The start host the on-domain filter anchors on
/// (same-host-plus-<c>www</c> by default, apex suffix with subdomains).</param>
/// <param name="IncludeSubdomains">Widen the on-domain match to a suffix match
/// on the apex host (a documented heuristic, not public-suffix-list-correct;
/// ADR-0081 rejected the eTLD+1 dependency).</param>
/// <param name="MaxDepth">The maximum hop distance from a start URL a Sweep
/// page may follow links from (the Job's parent-backlink-chain length);
/// <see cref="int.MaxValue"/> is unbounded.</param>
internal sealed record SweepPolicy(string AnchorHost, bool IncludeSubdomains, int MaxDepth);
