using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using WebReaper.Core.Crawling.Abstract;
using WebReaper.Core.Observability;
using WebReaper.Core.Observability.Abstract;
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
/// one pure method. Content extraction is the injected
/// <see cref="IContentExtractor"/> seam (ADR-0039) — never surfaced on
/// <see cref="ICrawlStep"/>; link extraction is the concrete
/// <see cref="LinkExtractor"/> function, called directly (ADR-0036 — one
/// adapter, never a real seam). ADR 0008: the seam's terminal is a typed
/// <c>JsonObject</c>; the legacy Newtonsoft <c>IContentParser</c> (JObject)
/// was removed at 6.0.0.
/// <para>
/// ADR-0018: emits <see cref="TraceEvent.ExtractionStarted"/> +
/// <see cref="TraceEvent.ExtractionCompleted"/> around the
/// <see cref="IContentExtractor.ExtractAsync"/> call on target pages
/// (chain-empty branch). Transit / pagination branches don't extract,
/// so they don't trace.
/// </para>
/// </summary>
internal sealed class CrawlStep : ICrawlStep
{
    private readonly IContentExtractor _extractor;
    private readonly IExtractionTrace _trace;

    public CrawlStep(IContentExtractor extractor, IExtractionTrace? trace = null)
    {
        _extractor = extractor;
        _trace = trace ?? Observability.Concrete.NullExtractionTrace.Instance;
    }

    public async ValueTask<CrawlOutcome> StepAsync(Job job, string document, Schema? schema)
    {
        var chain = job.LinkPathSelectors;

        // Empty selector chain ⇒ target page: parse with the Schema, no Jobs.
        if (chain.IsEmpty)
        {
            await _trace.RecordAsync(
                new TraceEvent.ExtractionStarted(HashSchema(schema)) { Url = job.Url });
            var data = await _extractor.ExtractAsync(document, schema);
            await _trace.RecordAsync(
                new TraceEvent.ExtractionCompleted(data) { Url = job.Url });
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

    // Stable schema-shape hash for the trace event. Null Schema (Markdown
    // extractor / no-schema strategy) returns null. We hash the schema's
    // ToString() — Schema is a deterministic record-ish shape; same shape
    // ⇒ same string. Compact 16-hex-char prefix so the trace JSONL stays
    // grep-able.
    private static string? HashSchema(Schema? schema)
    {
        if (schema is null) return null;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(schema.ToString() ?? ""));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
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
