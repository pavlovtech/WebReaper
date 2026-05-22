using WebReaper.Core.Crawling;
using WebReaper.Domain;

namespace WebReaper.Core.Spider.Abstract;

/// <summary>
/// The per-Job I/O shell (ADR-0022). Load one Job's page, run the Crawl step,
/// return a <see cref="JobReport"/>. The shell reports; the Crawl driver
/// (in-process <c>ScraperEngine</c> or the distributed worker) interprets the
/// report. Termination never travels as a thrown exception, and the shell no
/// longer owns the visited-link tracker, the crawl-limit stop, the
/// page-processor pipeline, or Sink fan-out.
/// </summary>
public interface ISpider
{
    /// <summary>
    /// Load <paramref name="job"/>'s page, run the Crawl step, and return the
    /// <see cref="JobReport"/> the Crawl driver interprets. Never throws to
    /// signal termination or the crawl limit (ADR-0022) — those are values on
    /// the report, not control flow. The report's child Jobs are
    /// <b>candidates</b>: visited-link dedup is the driver's, not the shell's.
    /// </summary>
    /// <param name="job">The Job to crawl; its selector chain decides
    /// parse-vs-follow-vs-paginate.</param>
    /// <param name="cancellationToken">Cancels the page load / crawl
    /// step.</param>
    /// <returns>The closed Job report — the <see cref="CrawlOutcome"/> plus the
    /// loaded document and the accounting facts the driver needs.</returns>
    Task<JobReport> CrawlAsync(Job job, CancellationToken cancellationToken = default);
}
