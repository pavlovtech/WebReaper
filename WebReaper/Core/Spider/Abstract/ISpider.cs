using WebReaper.Core.Crawling;
using WebReaper.Domain;

namespace WebReaper.Core.Spider.Abstract;

/// <summary>
/// The per-Job I/O shell (ADR-0022). Load one Job's page, run the Crawl step,
/// return a <see cref="JobReport"/>. The shell reports; the Crawl driver
/// (in-process <c>ScraperEngine</c> or the distributed worker) interprets the
/// report. Termination never travels as a thrown exception, and the shell no
/// longer owns the visited-link tracker, the crawl-limit stop, Sink fan-out,
/// or the PostProcessor / ScrapedData notification.
/// </summary>
public interface ISpider
{
    Task<JobReport> CrawlAsync(Job job, CancellationToken cancellationToken = default);
}
