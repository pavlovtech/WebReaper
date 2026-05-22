using WebReaper.Processing;

namespace WebReaper.Processing.Abstract;

/// <summary>
/// One stage of the page-processor pipeline (ADR-0038): consumer logic the
/// Crawl driver runs over a crawled target page's extracted record, in
/// registration order, <em>before</em> the Sink fan-out. Processor N is handed
/// processor N-1's <see cref="PageVerdict.Kept"/> record, so a stage can build
/// on an earlier one (validate the record an LLM stage repaired).
/// <para>
/// A processor <b>enriches</b> (mutate <c>context.Data.Data</c>, return
/// <see cref="PageVerdict.Keep"/>), <b>observes</b> (return <c>Keep</c> with the
/// record unchanged), <b>replaces / repairs</b> (return <c>Keep</c> with a
/// different <c>ParsedData</c> — the AI re-extraction case), or <b>filters</b>
/// (return <see cref="PageVerdict.Drop"/> — the pipeline stops and no Sink
/// emits the page). It replaces the ad-hoc <c>PostProcess</c> delegate; the
/// in-process observer <c>Subscribe</c> folded into <c>IScraperSink</c>.
/// </para>
/// <para>
/// Runs once per target page, on the crawl loop's threads — an implementation
/// holding shared state must be safe under concurrent calls (the
/// <c>IScraperSink</c> contract). Distinct pages are processed concurrently;
/// the processors of one page run sequentially. A processor needing one-time
/// async warm-up (an LLM processor opening an <c>IChatClient</c>) also
/// implements <c>IAsyncInitializable</c> (ADR-0033) — the Crawl driver warms it
/// before the crawl loop, with the schedulers, trackers and sinks. Registered
/// via <c>ScraperEngineBuilder.Process</c>.
/// </para>
/// </summary>
public interface IPageProcessor
{
    /// <summary>
    /// Process one extracted target page. Return <see cref="PageVerdict.Keep"/>
    /// to carry a record to the next stage (and ultimately the Sinks), or
    /// <see cref="PageVerdict.Drop"/> to filter the page out of the crawl's
    /// output. A thrown exception drops the page and is logged by the driver —
    /// a noisy page never aborts the crawl (ADR-0029); the one exception is
    /// <see cref="OperationCanceledException"/>, which propagates, cancellation
    /// being cooperative.
    /// </summary>
    /// <param name="context">The working record plus the page it came from —
    /// raw HTML, crawl ancestry, the parsing Schema.</param>
    /// <param name="cancellationToken">Cancels the crawl; an LLM call or other
    /// I/O inside a processor must honour it.</param>
    /// <returns>The verdict the Crawl driver acts on — keep the record or drop
    /// the page.</returns>
    ValueTask<PageVerdict> ProcessAsync(PageContext context, CancellationToken cancellationToken);
}
