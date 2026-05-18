using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using WebReaper.ConfigStorage.Abstract;
using WebReaper.Core.Crawling;
using WebReaper.Core.LinkTracker.Abstract;
using WebReaper.Core.Scheduler.Abstract;
using WebReaper.Core.Spider.Abstract;
using WebReaper.Domain;
using WebReaper.Extensions;
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Models;
using static WebReaper.Infra.Executor;

namespace WebReaper.Core;

/// <summary>
/// The in-process Crawl driver (ADR-0022). It owns the Crawl-global state the
/// per-Job <see cref="ISpider"/> shell deliberately does not: the visited-link
/// tracker, the crawl-limit stop rule, Sink fan-out, the PostProcessor /
/// ScrapedData notification, and the Outstanding-work latch (the
/// <c>pending</c> counter, in-memory <c>Interlocked</c> adapter). It seeds the
/// scheduler, drives <see cref="Parallel.ForEachAsync"/>, and interprets each
/// <see cref="JobReport"/> the shell returns. The crawl-limit stop is a value
/// the driver checks (soft/best-effort: in-flight iterations still finish) —
/// no longer a <c>PageCrawlLimitException</c> thrown through the fault-retry
/// policy.
/// </summary>
public class ScraperEngine
{
    public ScraperEngine(
        int parallelismDegree,
        IScraperConfigStorage configStorage,
        IScheduler jobScheduler,
        ISpider spider,
        IVisitedLinkTracker linkTracker,
        List<IScraperSink> sinks,
        ILogger logger,
        Action<ParsedData>? scrapedData = null,
        Func<Metadata, JsonObject, Task>? postProcessor = null)
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
    }

    private IScraperConfigStorage ConfigStorage { get; }
    private IScheduler Scheduler { get; }
    private ISpider Spider { get; }
    private IVisitedLinkTracker LinkTracker { get; }
    private List<IScraperSink> Sinks { get; }
    private ILogger Logger { get; }
    private Action<ParsedData>? ScrapedData { get; }
    private Func<Metadata, JsonObject, Task>? PostProcessor { get; }

    private int ParallelismDegree { get; }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await Scheduler.Initialization;
        await LinkTracker.Initialization;

        Logger.LogInformation("Start {class}.{method}", nameof(ScraperEngine), nameof(RunAsync));

        var config = await ConfigStorage.GetConfigAsync();

        // The Outstanding-work latch, in-memory adapter (ADR-0022): seed one
        // unit of credit per start URL. Children are credited BEFORE the parent
        // is decremented, so the counter can never hit zero prematurely
        // (the credit-conservation precondition). Only touched when
        // StopWhenDrained is on, so default behavior is unchanged.
        var pending = 0;

        foreach (var startUrl in config.StartUrls)
        {
            Logger.LogInformation("Scheduling the initial scraping job with start url {startUrl}", startUrl);

            if (config.StopWhenDrained) Interlocked.Increment(ref pending);

            await Scheduler.AddAsync(new Job(
                startUrl,
                config.LinkPathSelectors,
                ImmutableQueue.Create<string>(),
                config.StartPageType,
                config.PageActions), cancellationToken);
        }

        // No start urls at all — let the engine return right away.
        if (config.StopWhenDrained && Volatile.Read(ref pending) == 0)
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
                else
                {
                    await LinkTracker.AddVisitedLinkAsync(job.Url);

                    var report = await RetryAsync(() => Spider.CrawlAsync(job, cancellationToken));

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
                        // Candidate Jobs come back unfiltered; the driver owns
                        // the visited-link tracker, so the driver de-duplicates
                        // (ADR-0022 — the dedup that used to live in the shell).
                        var visited = (await LinkTracker.GetVisitedLinksAsync()).ToHashSet();
                        newJobs = report.Outcome.NextJobs
                            .Where(nextJob => !visited.Contains(nextJob.Url))
                            .ToList();
                    }
                }

                Logger.LogInformation("Received {JobsCount} new jobs", newJobs.Count);

                if (newJobs.Count > 0)
                {
                    // Account for children BEFORE this job is marked done,
                    // so the counter can never hit zero prematurely.
                    if (config.StopWhenDrained) Interlocked.Add(ref pending, newJobs.Count);

                    await Scheduler.AddAsync(newJobs, cancellationToken);
                }

                if (config.StopWhenDrained && Interlocked.Decrement(ref pending) == 0)
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
