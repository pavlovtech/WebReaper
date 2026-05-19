using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using WebReaper.ConfigStorage.Abstract;
using WebReaper.Core.Crawling;
using WebReaper.Core.Crawling.Abstract;
using WebReaper.Core.Crawling.Concrete;
using WebReaper.Core.LinkTracker.Abstract;
using WebReaper.Core.Scheduler.Abstract;
using WebReaper.Core.Spider.Abstract;
using WebReaper.Domain;
using WebReaper.Extensions;
using WebReaper.Infra.Abstract;
using WebReaper.Infra.Concrete;
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Models;

namespace WebReaper.Core;

/// <summary>
/// The in-process Crawl driver (ADR-0022). It owns the Crawl-global state the
/// per-Job <see cref="ISpider"/> shell deliberately does not: the visited-link
/// tracker, the crawl-limit stop rule, Sink fan-out, the PostProcessor /
/// ScrapedData notification, and the Outstanding-work latch (the
/// <c>pending</c> counter, in-memory <c>Interlocked</c> adapter). It seeds the
/// scheduler, drives <c>Parallel.ForEachAsync</c>, and interprets each
/// <see cref="JobReport"/> the shell returns. The crawl-limit stop is a value
/// the driver checks (soft/best-effort: in-flight iterations still finish) —
/// no longer a <c>PageCrawlLimitException</c> thrown through the fault-retry
/// policy.
/// </summary>
public class ScraperEngine
{
    /// <summary>
    /// Constructed by
    /// <see cref="WebReaper.Builders.ScraperEngineBuilder.BuildAsync"/> —
    /// consumers obtain an engine from the builder, not by calling this
    /// directly. Wires the in-process Crawl driver (ADR-0022): the scheduler,
    /// the persisted config storage, the per-Job <see cref="ISpider"/> shell,
    /// the visited-link idempotency authority, the Sink fan-out, the optional
    /// ScrapedData / PostProcessor callbacks, and the Outstanding-work latch
    /// (an in-memory <c>Interlocked</c> latch by default).
    /// </summary>
    internal ScraperEngine(
        int parallelismDegree,
        IScraperConfigStorage configStorage,
        IScheduler jobScheduler,
        ISpider spider,
        IVisitedLinkTracker linkTracker,
        List<IScraperSink> sinks,
        ILogger logger,
        Action<ParsedData>? scrapedData = null,
        Func<Metadata, JsonObject, Task>? postProcessor = null,
        IOutstandingWorkLatch? latch = null,
        IRetryPolicy? retryPolicy = null)
    {
        ParallelismDegree = parallelismDegree;
        ArgumentNullException.ThrowIfNull(configStorage);
        ArgumentNullException.ThrowIfNull(jobScheduler);
        ArgumentNullException.ThrowIfNull(spider);
        ArgumentNullException.ThrowIfNull(linkTracker);
        ArgumentNullException.ThrowIfNull(sinks);
        ArgumentNullException.ThrowIfNull(logger);

        (Scheduler, Spider, LinkTracker, Sinks, Logger, ConfigStorage) =
            (jobScheduler, spider, linkTracker, sinks, logger, configStorage);
        ScrapedData = scrapedData;
        PostProcessor = postProcessor;
        Latch = latch ?? new InMemoryOutstandingWorkLatch();
        // ADR-0026: the Crawl driver's retry around the per-Job Spider call
        // is now a named seam; default is the fixed-attempts core adapter
        // (one initial + three retries, pre-0026 behaviour minus the
        // cancellation-swallow bug).
        RetryPolicy = retryPolicy ?? new FixedAttemptsRetryPolicy();
    }

    private IScraperConfigStorage ConfigStorage { get; }
    private IScheduler Scheduler { get; }
    private ISpider Spider { get; }
    private IVisitedLinkTracker LinkTracker { get; }
    private List<IScraperSink> Sinks { get; }
    private ILogger Logger { get; }
    private Action<ParsedData>? ScrapedData { get; }
    private Func<Metadata, JsonObject, Task>? PostProcessor { get; }
    private IOutstandingWorkLatch Latch { get; }
    private IRetryPolicy RetryPolicy { get; }

    private int ParallelismDegree { get; }

    /// <summary>
    /// Run the crawl to completion: seed the scheduler from the config's start
    /// URLs and drive <c>Parallel.ForEachAsync</c> over the job stream until
    /// the Outstanding-work latch trips (all work drained) or the soft
    /// crawl-page limit is reached (ADR-0022 — termination is a value, never a
    /// thrown exception). Honours <paramref name="cancellationToken"/>.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await Scheduler.Initialization;
        await LinkTracker.Initialization;

        Logger.LogInformation("Start {class}.{method}", nameof(ScraperEngine), nameof(RunAsync));

        var config = await ConfigStorage.GetConfigAsync();

        var startUrls = config.StartUrls.ToList();

        // The Outstanding-work latch (ADR-0022 slice 3), behind its seam: seed
        // one unit of credit per start Job. Children are credited BEFORE the
        // parent's unit is returned (credit conservation — the counter can
        // never hit zero prematurely). Only driven when StopWhenDrained is on,
        // so default behavior is unchanged; the in-process adapter is the
        // slice-1 Interlocked counter.
        if (config.StopWhenDrained) await Latch.SeedAsync(startUrls.Count);

        foreach (var startUrl in startUrls)
        {
            Logger.LogInformation("Scheduling the initial scraping job with start url {startUrl}", startUrl);

            await Scheduler.AddAsync(new Job(
                startUrl,
                config.LinkPathSelectors,
                ImmutableQueue.Create<string>(),
                config.StartPageType,
                config.PageActions), cancellationToken);
        }

        // No start urls at all — let the engine return right away.
        if (config.StopWhenDrained && startUrls.Count == 0)
        {
            Scheduler.Complete();
        }

        var options = new ParallelOptions { MaxDegreeOfParallelism = ParallelismDegree };

        try
        {
            Logger.LogInformation("Start consuming the scraping jobs");

            await Parallel.ForEachAsync(Scheduler.GetAllAsync(cancellationToken), options, async (job, token) =>
            {
                Logger.LogInformation("Start crawling url {Url}", job.Url);

                // Crawl-global stop rule. Was a PageCrawlLimitException thrown
                // from the Spider shell and caught here; ADR-0022 makes it a
                // value the driver checks. Soft/best-effort: in-flight
                // iterations still finish (the universal crawler norm).
                if (await LinkTracker.GetVisitedLinksCount() >= config.PageCrawlLimit)
                {
                    Logger.LogInformation("Page crawl limit {Limit} reached; stopping", config.PageCrawlLimit);
                    Scheduler.Complete();
                    return;
                }

                List<Job> newJobs;

                if (config.UrlBlackList.Contains(job.Url))
                {
                    newJobs = new List<Job>();
                }
                else if (!await LinkTracker.TryAddVisitedLinkAsync(job.Url))
                {
                    // The idempotency authority (ADR-0022 slice 2): this URL
                    // was already visited — a duplicate discovery now, a
                    // redelivered Job once distributed. One atomic test-and-set
                    // makes it a no-op: no crawl, no children, no double
                    // emission. The Job's pending unit is still returned below,
                    // so the Outstanding-work latch stays balanced (credit
                    // conservation) — duplicates can't unbalance termination.
                    newJobs = new List<Job>();
                }
                else
                {
                    var report = await RetryPolicy.ExecuteAsync(
                        token => Spider.CrawlAsync(job, token), cancellationToken);

                    if (report.Outcome is CrawlOutcome.Parsed parsed)
                    {
                        await ProcessTargetPage(job, report.Document, parsed.Data, cancellationToken);
                        newJobs = new List<Job>();

                        if (await LinkTracker.GetVisitedLinksCount() >= config.PageCrawlLimit)
                        {
                            Logger.LogInformation("Page crawl limit {Limit} reached; stopping", config.PageCrawlLimit);
                            Scheduler.Complete();
                        }
                    }
                    else
                    {
                        // Children are enqueued UNFILTERED; discovery dedup is
                        // the per-Job TryAdd gate above when each child is
                        // itself processed (ADR-0022 slice 2 — one atomic
                        // membership check, not the old read-then-filter race).
                        newJobs = report.Outcome.NextJobs.ToList();
                    }
                }

                Logger.LogInformation("Received {JobsCount} new jobs", newJobs.Count);

                if (newJobs.Count > 0)
                {
                    // Credit children BEFORE this Job's unit is returned
                    // (credit conservation — ADR-0022).
                    if (config.StopWhenDrained) await Latch.AddAsync(newJobs.Count);

                    await Scheduler.AddAsync(newJobs, cancellationToken);
                }

                if (config.StopWhenDrained && await Latch.SignalProcessedAsync())
                {
                    Scheduler.Complete();
                }
            });
        }
        catch (TaskCanceledException ex)
        {
            Logger.LogWarning(ex, "Shutting down due to cancellation");
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Shutting down due to unhandled exception");
            throw;
        }
    }

    private async Task ProcessTargetPage(Job job, string doc, ParsedData result,
        CancellationToken cancellationToken = default)
    {
        Logger.LogInvocationCount();

        if (PostProcessor is not null)
            await PostProcessor.Invoke(new Metadata(job.ParentBacklinks.ToList(), job.Url, doc), result.Data);

        ScrapedData?.Invoke(result);

        Logger.LogInformation("Sending scraped data to sinks...");
        var sinkTasks = Sinks.Select(sink => sink.EmitAsync(result, cancellationToken));

        Logger.LogInformation("Waiting for sinks ...");
        await Task.WhenAll(sinkTasks);
        Logger.LogInformation("Finished waiting for sinks");
    }
}
