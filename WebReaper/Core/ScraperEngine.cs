using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using WebReaper.ConfigStorage.Abstract;
using WebReaper.Core.Blocking.Concrete;
using WebReaper.Core.Crawling;
using WebReaper.Core.Crawling.Abstract;
using WebReaper.Core.Crawling.Concrete;
using WebReaper.Core.LinkTracker.Abstract;
using WebReaper.Core.Scheduler.Abstract;
using WebReaper.Core.Spider.Abstract;
using WebReaper.Domain;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Telemetry;
using WebReaper.Extensions;
using WebReaper.Infra.Abstract;
using WebReaper.Infra.Concrete;
using WebReaper.Processing;
using WebReaper.Processing.Abstract;
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Models;

namespace WebReaper.Core;

/// <summary>
/// The in-process Crawl driver (ADR-0022). It owns the Crawl-global state the
/// per-Job <see cref="ISpider"/> shell deliberately does not: the visited-link
/// tracker, the crawl-limit stop rule, the page-processor pipeline, Sink
/// fan-out, and the Outstanding-work latch (the <c>pending</c> counter,
/// in-memory <c>Interlocked</c> adapter). It seeds the
/// scheduler, drives <c>Parallel.ForEachAsync</c>, and interprets each
/// <see cref="JobReport"/> the shell returns. The crawl-limit stop is a value
/// the driver checks; on a concluded Crawl the driver ends its own
/// consumption of the job stream (ADR-0037), cancelling whatever Jobs are in
/// flight at that moment — no longer a <c>PageCrawlLimitException</c> thrown
/// through the fault-retry policy.
/// <para>
/// ADR-0058: <see cref="IAsyncDisposable"/> dual of ADR-0033's
/// <see cref="IAsyncInitializable"/>. <see cref="DisposeAsync"/> walks
/// adapters in reverse warm-up order (processors → sinks → tracker →
/// scheduler → spider → builder-registered teardown hooks). Consumers
/// use <c>await using var engine = await builder.BuildAsync();</c> to
/// guarantee satellite-spawned subprocesses (CloakBrowser, …) tear down
/// on scope exit.
/// </para>
/// </summary>
public class ScraperEngine : IAsyncDisposable
{
    /// <summary>
    /// Constructed by
    /// <see cref="WebReaper.Builders.ScraperEngineBuilder.BuildAsync"/> —
    /// consumers obtain an engine from the builder, not by calling this
    /// directly. Wires the in-process Crawl driver (ADR-0022): the scheduler,
    /// the persisted config storage, the per-Job <see cref="ISpider"/> shell,
    /// the visited-link idempotency authority, the page-processor pipeline, the
    /// Sink fan-out, and the Outstanding-work latch (an in-memory
    /// <c>Interlocked</c> latch by default).
    /// </summary>
    internal ScraperEngine(
        int parallelismDegree,
        IScraperConfigStorage configStorage,
        IScheduler jobScheduler,
        ISpider spider,
        IVisitedLinkTracker linkTracker,
        List<IScraperSink> sinks,
        ILogger logger,
        IReadOnlyList<IPageProcessor>? pageProcessors = null,
        IOutstandingWorkLatch? latch = null,
        IRetryPolicy? retryPolicy = null,
        IReadOnlyList<IAsyncDisposable>? ownedDisposables = null,
        RunTelemetryHooks? telemetryHooks = null)
    {
        ParallelismDegree = parallelismDegree;
        ArgumentNullException.ThrowIfNull(configStorage);
        ArgumentNullException.ThrowIfNull(jobScheduler);
        ArgumentNullException.ThrowIfNull(spider);
        ArgumentNullException.ThrowIfNull(linkTracker);
        ArgumentNullException.ThrowIfNull(sinks);
        ArgumentNullException.ThrowIfNull(logger);

        (Scheduler, Spider, LinkTracker, Logger, ConfigStorage) =
            (jobScheduler, spider, linkTracker, logger, configStorage);
        // ADR-0076: the post-extraction surface — the page-processor pipeline,
        // the Sink fan-out, and the warm-up + disposal of those sinks and
        // processors — lives in one module the driver delegates to.
        _pipeline = new PostExtractionPipeline(sinks, pageProcessors, logger);
        Latch = latch ?? new InMemoryOutstandingWorkLatch();
        // ADR-0026: the Crawl driver's retry around the per-Job Spider call
        // is now a named seam; default is the fixed-attempts core adapter
        // (one initial + three retries, pre-0026 behaviour minus the
        // cancellation-swallow bug).
        RetryPolicy = retryPolicy ?? new FixedAttemptsRetryPolicy();
        // ADR-0058: builder-registered teardown hooks (satellite-spawned
        // subprocesses, e.g. CloakBrowser via .OnTeardown(...)). Empty by
        // default; the in-tree builder is the one v10.x caller that fills it.
        _ownedDisposables = ownedDisposables is { Count: > 0 }
            ? new List<IAsyncDisposable>(ownedDisposables)
            : new List<IAsyncDisposable>(0);
        // ADR-0066: per-run telemetry hooks (typically set by the
        // satellite via the builder's TelemetryHooks property). Null
        // when no LLM adapter ran; RunAsync returns RunReport(Llm: null).
        _telemetryHooks = telemetryHooks;
    }

    private IScraperConfigStorage ConfigStorage { get; }
    private IScheduler Scheduler { get; }
    private ISpider Spider { get; }
    private IVisitedLinkTracker LinkTracker { get; }
    private ILogger Logger { get; }
    // ADR-0076: owns the page-processor pipeline + Sink fan-out + their lifecycle.
    private readonly PostExtractionPipeline _pipeline;
    private IOutstandingWorkLatch Latch { get; }
    private IRetryPolicy RetryPolicy { get; }
    private readonly List<IAsyncDisposable> _ownedDisposables;
    private readonly RunTelemetryHooks? _telemetryHooks;
    private bool _disposed;

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
    public async Task<RunReport> RunAsync(CancellationToken cancellationToken = default)
    {
        // ADR-0066: reset telemetry at the start of each run so consecutive
        // RunAsync calls on the same engine produce independent reports.
        _telemetryHooks?.Reset();
        var sw = Stopwatch.StartNew();

        // ADR-0083: tally of residual-blocked pages suppressed (dropped) this
        // run — a Target/Sweep page the block drop policy (part 8) kept out of
        // the sinks. A single-element array so the parallel-loop closure mutates
        // the same cell; Interlocked.Increment makes the per-Job bump
        // thread-safe.
        var droppedBlockedPageCount = new int[1];

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
        // consume, so return without driving the loop. ADR-0066: still
        // returns a RunReport (likely empty if no work happened).
        if (stopRule.IsCrawlOver)
        {
            sw.Stop();
            return new RunReport(
                Llm: _telemetryHooks?.Snapshot(),
                Duration: sw.Elapsed,
                BlockedPageCount: droppedBlockedPageCount[0]);
        }

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

                    // ADR-0083 slice 3: suppress a residual-blocked Target/Sweep
                    // page before the Post-extraction pipeline and Sink fan-out,
                    // so challenge-page content never reaches a consumer's store.
                    // The block drop policy (part 8) is the confidence split —
                    // High drops always, Weak drops only when the page yielded no
                    // records — and every drop is tallied as a residual block.
                    // Detection reported (in the Spider); the driver acts, exactly
                    // as it already does for the visited-link and stop-rule
                    // verdicts (ADR-0022's line holds). A dropped Sweep page still
                    // follows its children: recovering real links from a blocked
                    // page is the loader's climb (a later slice), not this slice.
                    async Task EmitOrDropAsync(ParsedData data)
                    {
                        if (BlockDropPolicy.ShouldDrop(report.Block, CountRecords(data)))
                        {
                            Interlocked.Increment(ref droppedBlockedPageCount[0]);
                            Logger.LogInformation(
                                "Dropping residual-blocked page {Url}: {Reason}",
                                data.Url, report.Block.Reason);
                            return;
                        }

                        // ADR-0076: the page-processor pipeline + Sink fan-out is
                        // the Post-extraction pipeline's job; the driver only
                        // routes the record into it (and ignores the
                        // surviving-record return — the crawl path fires and
                        // forgets). ADR-0037: it runs on the iteration token, so a
                        // Crawl concluding mid-flight cancels the in-flight
                        // pipeline and fan-out too.
                        Logger.LogInvocationCount();
                        await _pipeline.ProcessAndEmitAsync(data, report.Page.Html,
                            job.ParentBacklinks.ToList(), config.ParsingScheme, token);
                    }

                    if (report.Outcome is CrawlOutcome.Parsed parsed)
                    {
                        await EmitOrDropAsync(parsed.Data);
                        newJobs = new List<Job>();
                    }
                    else if (report.Outcome is CrawlOutcome.Swept swept)
                    {
                        // ADR-0081: the Sweep page is the ONLY arm that both
                        // emits its record AND enqueues its on-domain children.
                        // The children retain the recursive sweep selector; the
                        // Visited-link tracker dedups them, which is also what
                        // terminates the sweep when the on-domain frontier
                        // saturates.
                        await EmitOrDropAsync(swept.Data);
                        newJobs = swept.Next.ToList();
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

        // ADR-0066: opaque-typed Llm snapshot — consumer casts to
        // WebReaper.AI.Llm.LlmTelemetrySnapshot when the AI satellite is
        // in use. Null when no satellite registered telemetry on the
        // builder (the no-LLM crawl path).
        sw.Stop();
        return new RunReport(
            Llm: _telemetryHooks?.Snapshot(),
            Duration: sw.Elapsed,
            BlockedPageCount: droppedBlockedPageCount[0]);
    }

    // ADR-0083 slice 3: how many records a Target/Sweep page actually extracted,
    // for the block drop policy's Weak "drop iff zero records" rule. A single
    // page yields one record, so this is 0 or 1 — it counts only when the
    // extraction produced real content. The Schema fold writes an empty string
    // for a selector that matched nothing (ADR-0029) and the page URL is always
    // folded in under "url" (ADR-0031), so neither an empty field nor the URL
    // counts: a challenge page parsed with a schema reads as zero records, which
    // is what lets the Weak rule suppress it.
    private static int CountRecords(ParsedData record)
    {
        foreach (var (key, value) in record.Data)
        {
            if (string.Equals(key, "url", StringComparison.Ordinal)) continue;
            if (IsNonEmpty(value)) return 1;
        }

        return 0;
    }

    private static bool IsNonEmpty(JsonNode? node) => node switch
    {
        null => false,
        // The fold writes "" for a non-matching string selector (ADR-0029); a
        // number / boolean (including 0 / false) is real data, as are non-empty
        // arrays and objects.
        JsonValue value => !(value.TryGetValue<string>(out var s) && string.IsNullOrEmpty(s)),
        JsonArray array => array.Count > 0,
        JsonObject obj => obj.Count > 0,
        _ => true,
    };

    // ADR-0033: warm up every adapter the driver holds that declares the
    // IAsyncInitializable capability — once, before the crawl loop. The driver
    // warms the scheduler and the visited-link tracker; the sinks and page
    // processors are warmed through the Post-extraction pipeline (ADR-0076),
    // which owns their lifecycle. InitializeAsync is idempotent, so a sink
    // shared with a distributed driver stays correct.
    private async Task WarmUpAdaptersAsync()
    {
        if (Scheduler is IAsyncInitializable scheduler)
            await scheduler.InitializeAsync();

        if (LinkTracker is IAsyncInitializable linkTracker)
            await linkTracker.InitializeAsync();

        // ADR-0076: warms every sink + processor that opts into ADR-0033.
        await _pipeline.InitializeAsync();
    }

    /// <summary>
    /// ADR-0058: dispose every adapter the driver holds that implements
    /// <see cref="IAsyncDisposable"/> or <see cref="IDisposable"/>, in
    /// REVERSE warm-up order (so dependents see their dependencies still
    /// valid mid-flush): the Post-extraction pipeline (page processors → sinks,
    /// ADR-0076) → link tracker → scheduler → spider. Then dispose
    /// builder-registered teardown hooks in LIFO order (satellite-spawned
    /// subprocesses like CloakBrowser). Per-adapter disposal exceptions are
    /// swallowed with a Warning log — a scrape that succeeded should not
    /// retroactively fail because a Redis-connection close timed out.
    /// </summary>
    /// <remarks>
    /// Idempotent: a second call short-circuits on <c>_disposed</c>.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // ADR-0076: the pipeline disposes its processors then sinks, each in
        // reverse registration order — the same order this driver did inline
        // pre-0076, now owned in one place.
        await _pipeline.DisposeAsync();
        await SafeDisposeAsync(LinkTracker);
        await SafeDisposeAsync(Scheduler);
        await SafeDisposeAsync(Spider);

        // LIFO over builder-registered hooks — a transport-spawned process
        // is usually a downstream resource of a tracker/sink that may have
        // been talking to it; tear them down in reverse registration order.
        for (var i = _ownedDisposables.Count - 1; i >= 0; i--)
            await SafeDisposeAsync(_ownedDisposables[i]);
    }

    private async ValueTask SafeDisposeAsync(object? obj)
    {
        try
        {
            switch (obj)
            {
                case IAsyncDisposable a: await a.DisposeAsync(); break;
                case IDisposable d: d.Dispose(); break;
            }
        }
        catch (Exception ex)
        {
            // Warning, not Error: scrape's outcome is the scrape's outcome;
            // teardown problems are observability, not run failure.
            Logger.LogWarning(ex, "Disposal of {Type} threw", obj?.GetType().Name ?? "(null)");
        }
    }
}
