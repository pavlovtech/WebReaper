using System.Collections.Immutable;
using WebReaper.Core.Crawling.Abstract;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Domain;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;
using WebReaper.Sinks.Models;

namespace WebReaper.Core.Crawling.Concrete;

/// <summary>
/// The crawl-step decision extracted from the old Spider.CrawlAsync. Holds the
/// page-category dispatch, the selector-chain advance/retain rule, link
/// extraction, content extraction, and child-Job provenance threading behind
/// one pure method. ADR-0081 adds the Sweep page: a recursive head selector
/// returns the <c>Swept</c> arm: extract this page AND enqueue its on-domain
/// child Jobs (the <see cref="SweepDomainFilter"/> boundary + depth cap from
/// the injected <see cref="SweepPolicy"/>), each retaining the sweep selector. Content extraction is the injected
/// <see cref="IContentExtractor"/> seam (ADR-0039) — never surfaced on
/// <see cref="ICrawlStep"/>; link extraction is the concrete
/// <see cref="LinkExtractor"/> function, called directly (ADR-0036 — one
/// adapter, never a real seam). ADR 0008: the seam's terminal is a typed
/// <c>JsonObject</c>; the legacy Newtonsoft <c>IContentParser</c> (JObject)
/// was removed at 6.0.0.
/// </summary>
internal sealed class CrawlStep : ICrawlStep
{
    private readonly IContentExtractor _extractor;
    // ADR-0081: the crawl-global on-domain + depth policy for the Sweep page
    // branch. Null on a non-sweep crawl (the recursive branch never fires).
    private readonly SweepPolicy? _sweepPolicy;

    public CrawlStep(IContentExtractor extractor, SweepPolicy? sweepPolicy = null)
    {
        _extractor = extractor;
        _sweepPolicy = sweepPolicy;
    }

    public async ValueTask<CrawlOutcome> StepAsync(Job job, string document, Schema? schema)
    {
        var chain = job.LinkPathSelectors;

        // Empty selector chain ⇒ target page: parse with the Schema, no Jobs.
        if (chain.IsEmpty)
        {
            var data = await _extractor.ExtractAsync(document, schema);
            return CrawlOutcome.Target(new ParsedData(job.Url, data));
        }

        var baseUrl = new Uri(job.Url);

        // Sweep page (ADR-0081): a recursive head selector ⇒ the one arm that
        // both extracts AND follows. Parse this page like a target page, then
        // enqueue its on-domain child Jobs which RETAIN the sweep selector (the
        // chain is left unchanged, never advanced), so the traversal
        // perpetuates until the Visited-link tracker frontier saturates or the
        // page-cap cutoff trips. The on-domain boundary and depth cap come from
        // the crawl-global SweepPolicy; with none, the boundary anchors on this
        // page's own host (transitively the start host on a single-host sweep)
        // and depth is unbounded.
        var head = chain.Peek();
        if (head.Recursive)
        {
            var record = await _extractor.ExtractAsync(document, schema);
            var sweptData = new ParsedData(job.Url, record);

            // Depth gate: the Job's parent-backlink-chain length IS its hop
            // distance from a start URL. At the cap, extract but follow nothing.
            var depth = job.ParentBacklinks.Count();
            var maxDepth = _sweepPolicy?.MaxDepth ?? int.MaxValue;
            if (depth >= maxDepth)
                return CrawlOutcome.Sweep(sweptData, ImmutableArray<Job>.Empty);

            var anchorHost = _sweepPolicy?.AnchorHost ?? baseUrl.Host;
            var includeSubdomains = _sweepPolicy?.IncludeSubdomains ?? false;

            var sweptLinks = await LinkExtractor.GetLinksAsync(baseUrl, document, head.Selector);
            var onDomain = sweptLinks.Where(
                link => SweepDomainFilter.IsOnDomain(link, anchorHost, includeSubdomains));
            // childChain = chain (unchanged) ⇒ RETAIN the recursive selector.
            var children = CreateJobs(job, head, chain, onDomain);

            return CrawlOutcome.Sweep(sweptData, children);
        }

        var advanced = chain.Dequeue(out var currentSelector);

        var itemLinks = await LinkExtractor.GetLinksAsync(baseUrl, document, currentSelector.Selector);
        var items = CreateJobs(job, currentSelector, advanced, itemLinks);

        // Pagination fires only when the consumed selector was the LAST one and
        // it paginates (mirrors the old (1, true) PageCategory case). Item Jobs
        // advance — `advanced` is now empty, so they are target pages. Next-page
        // Jobs retain the original one-element chain (the same step again).
        if (!(advanced.IsEmpty && currentSelector.HasPagination))
            return CrawlOutcome.Transit(items);

        var pageLinks = await LinkExtractor.GetLinksAsync(baseUrl, document, currentSelector.PaginationSelector!);
        var nextPages = CreateJobs(job, currentSelector, chain, pageLinks);

        return CrawlOutcome.Pagination(items, nextPages);
    }

    private static ImmutableArray<Job> CreateJobs(
        Job parent,
        LinkPathSelector currentSelector,
        ImmutableQueue<LinkPathSelector> childChain,
        IEnumerable<string> links) =>
        links
            .Select(link => parent with
            {
                Url = link,
                LinkPathSelectors = childChain,
                ParentBacklinks = parent.ParentBacklinks.Enqueue(parent.Url),
                PageType = currentSelector.PageType,
                PageActions = currentSelector.PageActions
            })
            .ToImmutableArray();
}
