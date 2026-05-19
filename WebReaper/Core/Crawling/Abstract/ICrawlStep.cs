using WebReaper.Core.Crawling;
using WebReaper.Domain;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Crawling.Abstract;

/// <summary>
/// The crawl step: the pure, in-process decision mapping a <see cref="Job"/> +
/// its already-loaded document + the parsing <see cref="Schema"/> to exactly
/// one <see cref="CrawlOutcome"/>.
///
/// No I/O behind this seam — no page loaders, visited-link tracker, sinks,
/// scheduler, or config storage, and no exception used as control flow (the
/// crawl-limit stop stays in the Spider shell). Deterministic in
/// (job, document, schema). Returned Jobs are <b>candidates</b> — not
/// visited-link filtered; the shell does that.
/// </summary>
public interface ICrawlStep
{
    /// <summary>
    /// Map the loaded Job to exactly one <see cref="CrawlOutcome"/> — parse
    /// the target page, follow links, or paginate — decided solely by the
    /// selector chain's shape. Pure and deterministic in
    /// (<paramref name="job"/>, <paramref name="document"/>,
    /// <paramref name="schema"/>); performs no I/O.
    /// </summary>
    /// <param name="job">The Job being crawled. Its selector chain — its
    /// length and whether the single remaining selector paginates — fully
    /// determines which <see cref="CrawlOutcome"/> arm is returned.</param>
    /// <param name="document">The already-loaded page body. The shell owns
    /// loading; the step never fetches.</param>
    /// <param name="schema">The Schema for a target page; may be null (no-op
    /// extraction). Ignored for transit / paginated pages.</param>
    ValueTask<CrawlOutcome> StepAsync(Job job, string document, Schema? schema);
}
