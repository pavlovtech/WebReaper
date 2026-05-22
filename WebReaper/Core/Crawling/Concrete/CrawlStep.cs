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
/// extraction, content parsing, and child-Job provenance threading behind one
/// pure method. Content parsing is the injected <see cref="IJsonContentParser"/>
/// seam — never surfaced on <see cref="ICrawlStep"/>; link extraction is the
/// concrete <see cref="LinkExtractor"/> function, called directly (ADR-0036 —
/// one adapter, never a real seam). ADR 0008: the content seam is the typed
/// <see cref="IJsonContentParser"/> (JsonObject); the legacy <c>IContentParser</c>
/// (JObject) was removed at 6.0.0.
/// </summary>
internal sealed class CrawlStep : ICrawlStep
{
    private readonly IJsonContentParser _contentParser;

    public CrawlStep(IJsonContentParser contentParser)
    {
        _contentParser = contentParser;
    }

    public async ValueTask<CrawlOutcome> StepAsync(Job job, string document, Schema? schema)
    {
        var chain = job.LinkPathSelectors;

        // Empty selector chain ⇒ target page: parse with the Schema, no Jobs.
        if (chain.IsEmpty)
        {
            var data = await _contentParser.ParseToJsonAsync(document, schema);
            return CrawlOutcome.Target(new ParsedData(job.Url, data));
        }

        var baseUrl = new Uri(job.Url);
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
