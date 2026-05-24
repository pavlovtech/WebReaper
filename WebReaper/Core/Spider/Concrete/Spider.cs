using System.Text;
using WebReaper.Core.Crawling;
using WebReaper.Core.Crawling.Abstract;
using WebReaper.Core.Loaders.Abstract;
using WebReaper.Core.Observability;
using WebReaper.Core.Observability.Abstract;
using WebReaper.Core.Spider.Abstract;
using WebReaper.Domain;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Spider.Concrete;

/// <summary>
/// The per-Job I/O shell around the <see cref="ICrawlStep"/> (ADR-0022):
/// load one Job's page, run the crawl step, return a <see cref="JobReport"/>.
/// Nothing else. The visited-link tracker, the crawl-limit stop, the
/// page-processor pipeline, and Sink fan-out are the Crawl driver's — not the
/// shell's. The shell <em>reports</em> what happened; the
/// driver <em>decides</em> what to do. No termination signal is thrown.
/// <para>
/// ADR-0034: the shell's two run-scoped inputs — the headless flag and the
/// parsing <see cref="Schema"/> — are supplied once, at construction. The
/// shell holds no <c>IScraperConfigStorage</c> and never re-reads the config
/// per Job; config storage is the Crawl driver's concern.
/// </para>
/// <para>
/// ADR-0018: the shell emits three <see cref="TraceEvent"/>s around the
/// page load — <see cref="TraceEvent.PageLoadStarted"/>,
/// <see cref="TraceEvent.PageLoadCompleted"/> or
/// <see cref="TraceEvent.PageLoadFailed"/>. The trace adapter is supplied
/// at construction; default is <c>NullExtractionTrace</c> (zero
/// allocation per event).
/// </para>
/// </summary>
internal class Spider : ISpider
{
    /// <param name="crawlStep">The pure crawl-step decision (ADR-0022).</param>
    /// <param name="pageLoader">The one page loader (ADR-0004).</param>
    /// <param name="headless">The crawl's headless setting, folded into every
    /// <see cref="PageRequest"/> the shell builds.</param>
    /// <param name="parsingScheme">The extraction <see cref="Schema"/> for
    /// target pages; <c>null</c> means no extraction.</param>
    /// <param name="trace">The trace adapter (ADR-0018). Defaults to
    /// the no-op when omitted; consumer wires a real adapter via
    /// <c>ScraperEngineBuilder.WithExtractionTrace</c> /
    /// <c>TraceToFile</c>.</param>
    public Spider(
        ICrawlStep crawlStep,
        IPageLoader pageLoader,
        bool headless,
        Schema? parsingScheme,
        IExtractionTrace? trace = null)
    {
        CrawlStep = crawlStep;
        PageLoader = pageLoader;
        Headless = headless;
        ParsingScheme = parsingScheme;
        Trace = trace ?? Observability.Concrete.NullExtractionTrace.Instance;
    }

    private IPageLoader PageLoader { get; }
    private ICrawlStep CrawlStep { get; }
    private bool Headless { get; }
    private Schema? ParsingScheme { get; }
    private IExtractionTrace Trace { get; }

    public async Task<JobReport> CrawlAsync(Job job, CancellationToken cancellationToken = default)
    {
        await Trace.RecordAsync(
            new TraceEvent.PageLoadStarted(job.PageType) { Url = job.Url },
            cancellationToken);

        string doc;
        try
        {
            doc = await PageLoader.LoadAsync(
                new PageRequest(job.Url, job.PageType, job.PageActions, Headless),
                cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Best-effort failure trace; never let the trace adapter
            // mask the original load exception.
            try
            {
                await Trace.RecordAsync(
                    new TraceEvent.PageLoadFailed(ex.GetType().Name, ex.Message) { Url = job.Url },
                    cancellationToken);
            }
            catch { /* swallowed; the load failure is what matters */ }
            throw;
        }

        // UTF-8 byte length — what a downstream replay tool would see on disk;
        // string.Length would mis-report for multi-byte content.
        await Trace.RecordAsync(
            new TraceEvent.PageLoadCompleted(Encoding.UTF8.GetByteCount(doc)) { Url = job.Url },
            cancellationToken);

        var outcome = await CrawlStep.StepAsync(job, doc, ParsingScheme);

        return new JobReport(outcome, doc);
    }
}
