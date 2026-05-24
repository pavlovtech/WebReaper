namespace WebReaper.Core.Observability.Abstract;

/// <summary>
/// ADR-0018. The page-lifecycle trace seam. Adapters consume
/// <see cref="TraceEvent"/>s from the Spider shell, CrawlStep, and the
/// Crawl driver — load, extract, process, emit. Three first-party
/// adapters:
/// <list type="bullet">
///   <item><c>NullExtractionTrace</c> — the no-op default; allocation-
///         free hot path via <see cref="ValueTask.CompletedTask"/>.</item>
///   <item><c>FileExtractionTrace</c> — the JSONL appender; the free
///         "what happened on Tuesday" answer.</item>
///   <item><c>WebReaper.Cloud.HostedExtractionTrace</c> — the deferred
///         paid satellite (REPOSITIONING-PLAN §3); not in v10.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ValueTask"/> on <see cref="RecordAsync"/> is load-bearing:
/// the no-op adapter completes synchronously (zero allocations); the
/// hot path stays free of <c>Task</c> boxing.
/// </para>
/// <para>
/// One <c>IExtractionTrace</c> per Crawl (no fan-out). A consumer
/// wanting two adapters wraps both in a small <c>Composite</c>
/// decorator they own — not a core seam.
/// </para>
/// </remarks>
public interface IExtractionTrace
{
    /// <summary>Record one trace event. Implementations must not throw
    /// in the happy path — the Spider shell and Crawl driver do not catch
    /// these calls; a throwing trace would surface as a scrape failure.
    /// Adapters that buffer (e.g. the file adapter's
    /// background-drain pattern) should enqueue here and never await a
    /// real I/O round-trip.</summary>
    ValueTask RecordAsync(TraceEvent ev, CancellationToken cancellationToken = default);
}
