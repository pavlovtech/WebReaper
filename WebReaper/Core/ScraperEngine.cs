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
/// the driver checks; on a concluded Crawl the driver ends its own
/// consumption of the job stream (ADR-0037), cancelling whatever Jobs are in
/// flight at that moment — no longer a <c>PageCrawlLimitException</c> thrown
/// through the fault-retry policy.
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
    /// the stop rule concludes the Crawl — the Outstanding-work latch trips
    /// (all work drained) or the soft crawl-page limit is reached. The driver
    /// then ends its own consumption of the stream (ADR-0037) and the run
    /// returns normally; a caller cancellation of
    /// <paramref name="cancellationToken"/> instead surfaces as a thrown
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await WarmUpAdaptersAsync();

        Logger.LogInformation("Start {class}.{method}", nameof(ScraperEngine), nameof(RunAsync));

        var config = await ConfigStorage.GetConfigAsync();

        var startUrls = config.StartUrls.ToList();

        // ADR-0032: the stop rule is the one home for "should this Crawl stop,
        // and why?" — it composes the Outstanding-work latch (drained) and the
        // soft page limit. The driver consults it; it never inlines latch
        // calls or limit arithmetic. Built per-run from the config it needs.
        var stopRule = new StopRule(
            Latch, LinkTracker, config.PageCrawlLimit, config.StopWhenDrained, Logger);

        await stopRule.SeedAsync(startUrls.Count);

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

        // ADR-0037: the Crawl was over before the loop began (no start URLs to
        // drain, or a resumed visited count already at the limit) — nothing to
        // consume, so return without driving the loop.
        if (stopRule.IsCrawlOver) return;

        // ADR-0037: the driver ends the Crawl by ceasing its OWN consumption,
        // not by asking the scheduler to stop producing (Scheduler.Complete()
        // was a no-op for every durable scheduler). This linked source is the
        // "Crawl concluded" signal: cancelling it ends GetAllAsync's stream —
        // every IScheduler adapter honours its GetAllAsync token — and unwinds
        // Parallel.ForEachAsync. Linked to the caller's token so a caller
        // cancellation still flows through.
        using var crawlCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = ParallelismDegree,
            CancellationToken = crawlCts.Token
        };

        try
        {
            Logger.LogInformation("Start consuming the scraping jobs");

            await Parallel.ForEachAsync(Scheduler.GetAllAsync(crawlCts.Token), options, async (job, token) =>
            {
                Logger.LogInformation("Start crawling url {Url}", job.Url);

                // The stop rule already concluded the Crawl — skip without
                // crawling. A Job dequeued in the window between the
                // conclusion and the loop unwinding still reaches a body here.
                if (stopRule.IsCrawlOver) return;

                List<Job> newJobs;

                if (config.UrlBlackList.Contains(job.Url))
                {
                    newJobs = new List<Job>();
                }
                else if (!await LinkTracker.TryAddVisitedLinkAsync(job.Url))
                {
                    // The idempotency authority (ADR-0022): this URL was
                    // already visited — a duplicate discovery now, a
                    // redelivered Job once distributed. One atomic test-and-set
                    // makes it a no-op: no crawl, no children, no double
                    // emission. The Job is still registered with the stop rule
                    // below, so the Outstanding-work latch stays balanced —
                    // duplicates can't unbalance termination.
                    newJobs = new List<Job>();
                }
                else
                {
                    // ADR-0037: per-Job work runs on the iteration token, so a
                    // Crawl concluding mid-flight cancels the in-flight Spider
                    // calls (the Retry policy never retries OCE — ADR-0026 —
                    // so the abort is not retry-amplified).
                    var report = await RetryPolicy.ExecuteAsync(
                        ct => Spider.CrawlAsync(job, ct), token);

                    if (report.Outcome is CrawlOutcome.Parsed parsed)
                    {
                        await ProcessTargetPage(job, report.Document, parsed.Data, token);
                        newJobs = new List<Job>();
                    }
                    else
                    {
                        // Children are enqueued UNFILTERED; discovery dedup is
                        // the per-Job TryAdd gate above when each child is
                        // itself processed (ADR-0022 — one atomic membership
                        // check, not the old read-then-filter race).
                        newJobs = report.Outcome.NextJobs.ToList();
                    }
                }

                Logger.LogInformation("Received {JobsCount} new jobs", newJobs.Count);

                // Register this Job with the stop rule BEFORE enqueueing its
                // children: the Outstanding-work latch must credit the children
                // before they can be dequeued, or a fast worker could draw the
                // count to zero before a child's credit exists (credit
                // conservation). RegisterProcessedAsync credits the children
                // and returns this Job's unit in one atomic step.
                var concluded = await stopRule.RegisterProcessedAsync(newJobs.Count);

                if (newJobs.Count > 0)
                    await Scheduler.AddAsync(newJobs, token);

                // ADR-0037: this Job's registration concluded the Crawl. Cancel
                // the driver's own consumption — GetAllAsync stops yielding and
                // the parallel loop unwinds with an OperationCanceledException,
                // caught below as the normal end of a finished Crawl.
                if (concluded) crawlCts.Cancel();
            });
        }
        catch (OperationCanceledException)
            when (crawlCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // ADR-0037: the driver cancelled its own consumption because the
            // stop rule concluded the Crawl (all work drained, or the soft
            // page limit reached). The caller's token is still live, so this
            // is the normal, successful end of the Crawl — not a fault, not a
            // caller cancellation. Swallow and return.
            Logger.LogInformation("Crawl complete");
        }
        catch (OperationCanceledException ex)
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

    // ADR-0033: warm up every adapter the driver holds that declares the
    // IAsyncInitializable capability — the scheduler, the visited-link tracker
    // and every sink — once, before the crawl loop (the IHostedService model).
    // Adapters with no async warm-up (the in-memory defaults, the console and
    // file sinks) implement nothing and are skipped. InitializeAsync is
    // idempotent, so a sink shared with a distributed driver stays correct.
    private async Task WarmUpAdaptersAsync()
    {
        if (Scheduler is IAsyncInitializable scheduler)
            await scheduler.InitializeAsync();

        if (LinkTracker is IAsyncInitializable linkTracker)
            await linkTracker.InitializeAsync();

        foreach (var sink in Sinks)
            if (sink is IAsyncInitializable initializableSink)
                await initializableSink.InitializeAsync();
    }

    private async Task ProcessTargetPage(Job job, string doc, ParsedData result,
        CancellationToken cancellationToken = default)
    {
        Logger.LogInvocationCount();

        if (PostProcessor is not null)
            await PostProcessor.Invoke(new Metadata(job.ParentBacklinks.ToList(), job.Url, doc), result.Data);

        ScrapedData?.Invoke(result);

        Logger.LogInformation("Sending scraped data to sinks...");
        // ADR-0031: hand each sink its own deep-cloned Data. The fan-out runs
        // sinks concurrently (Task.WhenAll) and JsonObject is not thread-safe,
        // so a shared instance would race — e.g. CosmosSink writes "id", and
        // BufferedFileSink queues the object to drain later. The `with` copy
        // bypasses ParsedData's merge initializer (the clone is of the
        // already-merged Data), so there is no double-merge.
        var sinkTasks = Sinks.Select(sink => sink.EmitAsync(
            result with { Data = (JsonObject)result.Data.DeepClone() }, cancellationToken));

        Logger.LogInformation("Waiting for sinks ...");
        await Task.WhenAll(sinkTasks);
        Logger.LogInformation("Finished waiting for sinks");
    }
}
