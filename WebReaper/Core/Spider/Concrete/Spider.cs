using WebReaper.ConfigStorage.Abstract;
using WebReaper.Core.Crawling;
using WebReaper.Core.Crawling.Abstract;
using WebReaper.Core.Loaders.Abstract;
using WebReaper.Core.Spider.Abstract;
using WebReaper.Domain;

namespace WebReaper.Core.Spider.Concrete;

/// <summary>
/// The per-Job I/O shell around the <see cref="ICrawlStep"/> (ADR-0022):
/// load one Job's page, run the crawl step, return a <see cref="JobReport"/>.
/// Nothing else. The visited-link tracker, the crawl-limit stop, Sink
/// fan-out, and the PostProcessor / ScrapedData notification are the Crawl
/// driver's — not the shell's. The shell <em>reports</em> what happened; the
/// driver <em>decides</em> what to do. No termination signal is thrown.
/// </summary>
internal class Spider : ISpider
{
    public Spider(
        ICrawlStep crawlStep,
        IPageLoader pageLoader,
        IScraperConfigStorage configStorage)
    {
        CrawlStep = crawlStep;
        PageLoader = pageLoader;
        ScraperConfigStorage = configStorage;
    }

    private IPageLoader PageLoader { get; }
    private ICrawlStep CrawlStep { get; }
    private IScraperConfigStorage ScraperConfigStorage { get; }

    public async Task<JobReport> CrawlAsync(Job job, CancellationToken cancellationToken = default)
    {
        var config = await ScraperConfigStorage.GetConfigAsync();

        var doc = await PageLoader.LoadAsync(
            new PageRequest(job.Url, job.PageType, job.PageActions, config.Headless),
            cancellationToken);

        var outcome = await CrawlStep.StepAsync(job, doc, config.ParsingScheme);

        return new JobReport(outcome, doc);
    }
}
