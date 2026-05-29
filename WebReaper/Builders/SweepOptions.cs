namespace WebReaper.Builders;

/// <summary>
/// Options for a Site sweep (ADR-0081): the whole-site recursive crawl
/// appended by <see cref="ScraperEngineBuilder.Sweep"/>. All optional; the
/// defaults are the firecrawl-shaped "crawl this site" behaviour (every anchor,
/// same host plus <c>www</c>, unbounded depth, sitemap-seeded). The page cap is
/// the existing <see cref="ScraperEngineBuilder.PageCrawlLimit"/> (it maps onto
/// the Stop rule's cutoff, ADR-0032), not a field here.
/// </summary>
public sealed record SweepOptions
{
    /// <summary>The links to sweep; defaults to <c>a[href]</c> (every anchor).
    /// Restrict it (for example <c>a[href^='/blog/']</c>) to sweep one
    /// section.</summary>
    public string LinkSelector { get; init; } = "a[href]";

    /// <summary>Widen the on-domain boundary from same-host-plus-<c>www</c> to a
    /// suffix match on the apex host, a documented heuristic, not
    /// public-suffix-list-correct (ADR-0081). Default <c>false</c>.</summary>
    public bool IncludeSubdomains { get; init; }

    /// <summary>The maximum hop distance from a start URL the sweep follows
    /// links from (a page's parent-backlink-chain length). <c>null</c> is
    /// unbounded (the default).</summary>
    public int? MaxDepth { get; init; }

    /// <summary>Also seed the sweep frontier from the Site mapper's discovered
    /// sitemap URLs (ADR-0042), so a site with a sitemap but sparse internal
    /// linking is still covered. Default <c>true</c>; the two discovery modes
    /// union through the same Visited-link tracker, so seeds and followed links
    /// dedup against each other.</summary>
    public bool Sitemap { get; init; } = true;
}
