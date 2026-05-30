using WebReaper.Core.Blocking.Abstract;
using WebReaper.Core.Crawling;
using WebReaper.Core.Crawling.Abstract;
using WebReaper.Core.Loaders.Abstract;
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
/// </summary>
internal class Spider : ISpider
{
    /// <param name="crawlStep">The pure crawl-step decision (ADR-0022).</param>
    /// <param name="pageLoader">The one page loader (ADR-0004).</param>
    /// <param name="blockDetector">The block detector (ADR-0083): classifies the
    /// loaded page as a bot-check challenge (reporting, not acting). The shell
    /// runs it on every load and carries the verdict on the
    /// <see cref="JobReport"/>.</param>
    /// <param name="headless">The crawl's headless setting, folded into every
    /// <see cref="PageRequest"/> the shell builds.</param>
    /// <param name="parsingScheme">The extraction <see cref="Schema"/> for
    /// target pages; <c>null</c> means no extraction.</param>
    public Spider(
        ICrawlStep crawlStep,
        IPageLoader pageLoader,
        IBlockDetector blockDetector,
        bool headless,
        Schema? parsingScheme)
    {
        CrawlStep = crawlStep;
        PageLoader = pageLoader;
        BlockDetector = blockDetector;
        Headless = headless;
        ParsingScheme = parsingScheme;
    }

    private IPageLoader PageLoader { get; }
    private ICrawlStep CrawlStep { get; }
    private IBlockDetector BlockDetector { get; }
    private bool Headless { get; }
    private Schema? ParsingScheme { get; }

    public async Task<JobReport> CrawlAsync(Job job, CancellationToken cancellationToken = default)
    {
        var page = await PageLoader.LoadAsync(
            new PageRequest(job.Url, job.PageType, job.PageActions, Headless),
            cancellationToken);

        // ADR-0083: classify the load before the crawl step runs (the verdict is
        // independent of the outcome). Pure and non-throwing; the driver reads it.
        var block = BlockDetector.Detect(page);

        var outcome = await CrawlStep.StepAsync(job, page.Html, ParsingScheme);

        return new JobReport(outcome, page, block);
    }
}
