using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.ConfigStorage.Abstract;
using WebReaper.Core;
using WebReaper.Core.Crawling;
using WebReaper.Core.LinkTracker.Concrete;
using WebReaper.Core.Scheduler.Abstract;
using WebReaper.Core.Scheduler.Concrete;
using WebReaper.Core.Spider.Abstract;
using WebReaper.Domain;
using WebReaper.Domain.Selectors;
using WebReaper.Infra.Abstract;
using WebReaper.Processing;
using WebReaper.Processing.Abstract;
using WebReaper.Processing.Concrete;
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Concrete;
using WebReaper.Sinks.Models;

namespace WebReaper.UnitTests;

// ADR-0022: the in-process Crawl driver interpreting a JobReport offline.
// Orchestration that used to be integration-only — the limit stop, discovery
// dedup, and "fan out / notify iff a Target page" — is now pinned through the
// seam, because the shell returns a value the driver acts on instead of
// emitting via events and throwing to terminate.
public class ScraperEngineDriverTests
{
    private sealed class FakeConfigStorage(ScraperConfig config) : IScraperConfigStorage
    {
        public Task CreateConfigAsync(ScraperConfig c) => Task.CompletedTask;
        public Task<ScraperConfig> GetConfigAsync() => Task.FromResult(config);
    }

    private sealed class ScriptedSpider(Func<Job, JobReport> script) : ISpider
    {
        public readonly List<string> Crawled = new();

        public Task<JobReport> CrawlAsync(Job job, CancellationToken cancellationToken = default)
        {
            lock (Crawled) Crawled.Add(job.Url);
            return Task.FromResult(script(job));
        }
    }

    private sealed class RecordingSink : IScraperSink
    {
        public readonly List<ParsedData> Emitted = new();
        public bool DataCleanupOnStart { get; set; }

        public Task EmitAsync(ParsedData entity, CancellationToken cancellationToken = default)
        {
            lock (Emitted) Emitted.Add(entity);
            return Task.CompletedTask;
        }
    }

    // ADR-0033: a sink that opts into the IAsyncInitializable warm-up
    // capability — it records the order of warm-up vs emit and how many times
    // the warm-up body actually ran.
    private sealed class WarmUpRecordingSink : IScraperSink, IAsyncInitializable
    {
        public readonly List<string> Calls = new();
        private int _coreRuns;
        public int CoreRuns => Volatile.Read(ref _coreRuns);

        private readonly Lazy<Task> _initialization;
        public WarmUpRecordingSink() => _initialization = new Lazy<Task>(InitializeCoreAsync);

        public bool DataCleanupOnStart { get; set; }

        public Task InitializeAsync() => _initialization.Value;

        private Task InitializeCoreAsync()
        {
            Interlocked.Increment(ref _coreRuns);
            lock (Calls) Calls.Add("init");
            return Task.CompletedTask;
        }

        public Task EmitAsync(ParsedData entity, CancellationToken cancellationToken = default)
        {
            lock (Calls) Calls.Add("emit");
            return Task.CompletedTask;
        }
    }

    // ADR-0037: a durable-style scheduler — its GetAllAsync is a poll loop
    // that ends ONLY when its token is cancelled, never by self-completion,
    // exactly like FileScheduler / RedisScheduler / SqliteScheduler. It has no
    // Complete() (the interface no longer has one). A crawl over it terminates
    // only because the Crawl driver cancels its own consumption; before
    // ADR-0037 it would hang forever.
    private sealed class PollingScheduler : IScheduler
    {
        private readonly ConcurrentQueue<Job> _jobs = new();

        public bool DataCleanupOnStart { get; set; }

        public Task AddAsync(Job job, CancellationToken cancellationToken = default)
        {
            _jobs.Enqueue(job);
            return Task.CompletedTask;
        }

        public Task AddAsync(IEnumerable<Job> jobs, CancellationToken cancellationToken = default)
        {
            foreach (var job in jobs) _jobs.Enqueue(job);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<Job> GetAllAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_jobs.TryDequeue(out var job))
                    yield return job;
                else
                    await Task.Delay(10, cancellationToken);
            }
        }
    }

    private static ScraperConfig Config(int limit = int.MaxValue, bool stopWhenDrained = true) => new(
        ParsingScheme: null,
        LinkPathSelectors: ImmutableQueue<LinkPathSelector>.Empty,
        StartUrls: new[] { "root" },
        UrlBlackList: Array.Empty<string>(),
        PageCrawlLimit: limit,
        StopWhenDrained: stopWhenDrained);

    private static JobReport Followed(params string[] urls) =>
        new(CrawlOutcome.Transit(urls
                .Select(u => new Job(u, ImmutableQueue<LinkPathSelector>.Empty, ImmutableQueue<string>.Empty))
                .ToImmutableArray()),
            string.Empty);

    private static JobReport Parsed(string url) =>
        new(CrawlOutcome.Target(new ParsedData(url, new JsonObject())), "<html/>");

    private static JobReport Swept(string url, params string[] children) =>
        new(CrawlOutcome.Sweep(
                new ParsedData(url, new JsonObject()),
                children
                    .Select(u => new Job(u,
                        ImmutableQueue.CreateRange(new[] { LinkPathSelector.Sweep() }),
                        ImmutableQueue<string>.Empty))
                    .ToImmutableArray()),
            "<html/>");

    [Fact]
    public async Task Swept_page_both_emits_its_record_and_follows_its_children_until_the_frontier_saturates()
    {
        // ADR-0081: the Sweep page is the one arm that BOTH emits AND follows.
        // A cyclic 3-page graph; the Visited-link tracker dedups so each page is
        // crawled and emitted exactly once, and the recursive frontier
        // saturates so the crawl terminates (a hang would fail WaitAsync).
        var sink = new RecordingSink();

        var spider = new ScriptedSpider(job => job.Url switch
        {
            "root" => Swept("root", "a", "b"),
            "a" => Swept("a", "b", "root"),   // cycles back to seen pages
            "b" => Swept("b", "a"),           // cycles
            _ => Swept(job.Url),
        });

        var engine = new ScraperEngine(
            parallelismDegree: 1,
            new FakeConfigStorage(Config()),
            new InMemoryScheduler(),
            spider,
            new InMemoryVisitedLinkTracker(),
            new List<IScraperSink> { sink },
            NullLogger.Instance);

        await engine.RunAsync().WaitAsync(TimeSpan.FromSeconds(10));

        // Each page crawled once (dedup) AND emitted once (Swept emits like a
        // target page), proof the driver runs the Post-extraction pipeline for
        // the Swept arm, not only for Parsed.
        Assert.Equal(new[] { "a", "b", "root" }, sink.Emitted.Select(p => p.Url).OrderBy(u => u));
        Assert.Equal(3, spider.Crawled.Count);
    }

    [Fact]
    public async Task Crawl_limit_stops_the_run_as_a_value_never_an_exception()
    {
        // PageCrawlLimit 0: the driver's limit gate trips before the first
        // crawl. Pre-ADR-0022 this was a PageCrawlLimitException thrown from
        // the shell and run through the Crawl driver's 3x retry (the retry
        // is now a named IRetryPolicy seam, ADR-0026); now the limit is a
        // value the driver checks and the run ends cleanly (WaitAsync would
        // surface a throw or a hang as a failure).
        var spider = new ScriptedSpider(_ => Followed());
        var engine = new ScraperEngine(
            parallelismDegree: 4,
            new FakeConfigStorage(Config(limit: 0, stopWhenDrained: false)),
            new InMemoryScheduler(),
            spider,
            new InMemoryVisitedLinkTracker(),
            new List<IScraperSink>(),
            NullLogger.Instance);

        await engine.RunAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(spider.Crawled);
    }

    [Fact]
    public async Task Already_visited_child_is_not_re_enqueued()
    {
        var tracker = new InMemoryVisitedLinkTracker();
        await tracker.AddVisitedLinkAsync("child-A"); // already seen

        var spider = new ScriptedSpider(job =>
            job.Url == "root" ? Followed("child-A", "child-B") : Followed());

        var engine = new ScraperEngine(
            parallelismDegree: 1,
            new FakeConfigStorage(Config()),
            new InMemoryScheduler(),
            spider,
            tracker,
            new List<IScraperSink>(),
            NullLogger.Instance);

        await engine.RunAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains("root", spider.Crawled);
        Assert.Contains("child-B", spider.Crawled);
        Assert.DoesNotContain("child-A", spider.Crawled); // driver de-duplicated it
    }

    [Fact]
    public async Task Sink_fan_out_and_page_processors_run_only_for_target_pages()
    {
        // ADR-0038: the page-processor pipeline, like the Sink fan-out, runs
        // only for a Target page — never a Transit page.
        var sink = new RecordingSink();
        var processed = new List<string>();

        var spider = new ScriptedSpider(job =>
            job.Url == "root" ? Followed("item") : Parsed(job.Url));

        var processor = new DelegatePageProcessor((ctx, _) =>
        {
            lock (processed) processed.Add(ctx.Data.Url);
            return ValueTask.FromResult(PageVerdict.Keep(ctx.Data));
        });

        var engine = new ScraperEngine(
            parallelismDegree: 1,
            new FakeConfigStorage(Config()),
            new InMemoryScheduler(),
            spider,
            new InMemoryVisitedLinkTracker(),
            new List<IScraperSink> { sink },
            NullLogger.Instance,
            pageProcessors: new IPageProcessor[] { processor });

        await engine.RunAsync().WaitAsync(TimeSpan.FromSeconds(10));

        // "root" is Followed -> no processing/emission; only "item" is Parsed.
        Assert.Equal(new[] { "item" }, sink.Emitted.Select(p => p.Url));
        Assert.Equal(new[] { "item" }, processed);
    }

    [Fact]
    public async Task Duplicate_discovered_url_is_crawled_once_and_the_latch_stays_balanced()
    {
        // "root" discovers the SAME url twice. Children are enqueued
        // unfiltered; the per-Job TryAdd idempotency gate makes the second a
        // no-op. The run must still terminate (StopWhenDrained) — proof the
        // Outstanding-work latch stays balanced even though a Job was a
        // duplicate no-op (credit conservation: 3 enqueued, 3 returned).
        var spider = new ScriptedSpider(job =>
            job.Url == "root" ? Followed("dup", "dup") : Followed());

        var engine = new ScraperEngine(
            parallelismDegree: 1,
            new FakeConfigStorage(Config()),
            new InMemoryScheduler(),
            spider,
            new InMemoryVisitedLinkTracker(),
            new List<IScraperSink>(),
            NullLogger.Instance);

        await engine.RunAsync().WaitAsync(TimeSpan.FromSeconds(10)); // a hang => unbalanced latch => fail

        Assert.Equal(1, spider.Crawled.Count(u => u == "dup")); // crawled once; second was a no-op
        Assert.Contains("root", spider.Crawled);
    }

    [Fact]
    public async Task Each_sink_receives_its_own_clone_of_the_parsed_data()
    {
        // ADR-0031: the fan-out deep-clones Data per sink, so the concurrent
        // sinks never share a JsonObject. Two sinks must receive distinct
        // ParsedData with distinct Data — and both clones carry the url folded
        // in at ParsedData construction.
        var sinkA = new RecordingSink();
        var sinkB = new RecordingSink();

        var spider = new ScriptedSpider(job =>
            job.Url == "root" ? Followed("item") : Parsed(job.Url));

        var engine = new ScraperEngine(
            parallelismDegree: 1,
            new FakeConfigStorage(Config()),
            new InMemoryScheduler(),
            spider,
            new InMemoryVisitedLinkTracker(),
            new List<IScraperSink> { sinkA, sinkB },
            NullLogger.Instance);

        await engine.RunAsync().WaitAsync(TimeSpan.FromSeconds(10));

        var a = Assert.Single(sinkA.Emitted);
        var b = Assert.Single(sinkB.Emitted);
        Assert.NotSame(a, b);
        Assert.NotSame(a.Data, b.Data);
        Assert.Equal("item", a.Data["url"]!.GetValue<string>());
        Assert.Equal("item", b.Data["url"]!.GetValue<string>());
    }

    [Fact]
    public async Task Driver_warms_up_an_initializable_sink_before_the_first_emit()
    {
        // ADR-0033: the Crawl driver calls InitializeAsync on every sink that
        // declares the IAsyncInitializable capability, once, before the crawl
        // loop — so warm-up always precedes the first EmitAsync.
        var sink = new WarmUpRecordingSink();

        var spider = new ScriptedSpider(job =>
            job.Url == "root" ? Followed("item") : Parsed(job.Url));

        var engine = new ScraperEngine(
            parallelismDegree: 1,
            new FakeConfigStorage(Config()),
            new InMemoryScheduler(),
            spider,
            new InMemoryVisitedLinkTracker(),
            new List<IScraperSink> { sink },
            NullLogger.Instance);

        await engine.RunAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(new[] { "init", "emit" }, sink.Calls);
        Assert.Equal(1, sink.CoreRuns);
    }

    [Fact]
    public async Task InitializeAsync_runs_the_warm_up_once_under_concurrent_calls()
    {
        // ADR-0033: warm-up is idempotent — Lazy<Task> runs the body once and
        // hands every caller the same task, so a per-message distributed
        // driver may call InitializeAsync freely.
        var sink = new WarmUpRecordingSink();

        await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => sink.InitializeAsync())));

        Assert.Equal(1, sink.CoreRuns);
    }

    [Fact]
    public async Task Page_processors_run_in_order_each_seeing_the_previous_output()
    {
        // ADR-0038: an ordered pipeline — processor N sees processor N-1's
        // record. p1 stamps "a"; p2 asserts "a" is already there, then stamps
        // "b"; the sink sees both fields.
        var sink = new RecordingSink();

        var spider = new ScriptedSpider(job =>
            job.Url == "root" ? Followed("item") : Parsed(job.Url));

        var p1 = new DelegatePageProcessor((ctx, _) =>
        {
            ctx.Data.Data["a"] = 1;
            return ValueTask.FromResult(PageVerdict.Keep(ctx.Data));
        });
        var p2 = new DelegatePageProcessor((ctx, _) =>
        {
            Assert.Equal(1, ctx.Data.Data["a"]!.GetValue<int>()); // p1 ran first
            ctx.Data.Data["b"] = 2;
            return ValueTask.FromResult(PageVerdict.Keep(ctx.Data));
        });

        var engine = new ScraperEngine(
            parallelismDegree: 1,
            new FakeConfigStorage(Config()),
            new InMemoryScheduler(),
            spider,
            new InMemoryVisitedLinkTracker(),
            new List<IScraperSink> { sink },
            NullLogger.Instance,
            pageProcessors: new IPageProcessor[] { p1, p2 });

        await engine.RunAsync().WaitAsync(TimeSpan.FromSeconds(10));

        var emitted = Assert.Single(sink.Emitted);
        Assert.Equal(1, emitted.Data["a"]!.GetValue<int>());
        Assert.Equal(2, emitted.Data["b"]!.GetValue<int>());
    }

    [Fact]
    public async Task Processor_Drop_filters_the_page_so_no_sink_emits_it()
    {
        // ADR-0038: a processor returning Drop ends the pipeline — no sink
        // emits the page — and the crawl still terminates.
        var sink = new RecordingSink();

        var spider = new ScriptedSpider(job =>
            job.Url == "root" ? Followed("item") : Parsed(job.Url));

        var dropAll = new DelegatePageProcessor((_, _) =>
            ValueTask.FromResult(PageVerdict.Drop("test: drop everything")));

        var engine = new ScraperEngine(
            parallelismDegree: 1,
            new FakeConfigStorage(Config()),
            new InMemoryScheduler(),
            spider,
            new InMemoryVisitedLinkTracker(),
            new List<IScraperSink> { sink },
            NullLogger.Instance,
            pageProcessors: new IPageProcessor[] { dropAll });

        await engine.RunAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(sink.Emitted); // the page was filtered out
    }

    [Fact]
    public async Task Processor_that_throws_drops_only_that_page_and_the_crawl_continues()
    {
        // ADR-0038: a processor throwing (anything but OperationCanceledException)
        // drops that page and is logged — the crawl continues and other pages
        // still emit. A noisy page never aborts the crawl (ADR-0029).
        var sink = new RecordingSink();

        var spider = new ScriptedSpider(job =>
            job.Url == "root" ? Followed("good", "bad") : Parsed(job.Url));

        var throwOnBad = new DelegatePageProcessor((ctx, _) =>
            ctx.Data.Url == "bad"
                ? throw new InvalidOperationException("test: processor blew up")
                : ValueTask.FromResult(PageVerdict.Keep(ctx.Data)));

        var engine = new ScraperEngine(
            parallelismDegree: 1,
            new FakeConfigStorage(Config()),
            new InMemoryScheduler(),
            spider,
            new InMemoryVisitedLinkTracker(),
            new List<IScraperSink> { sink },
            NullLogger.Instance,
            pageProcessors: new IPageProcessor[] { throwOnBad });

        await engine.RunAsync().WaitAsync(TimeSpan.FromSeconds(10));

        // "bad" was dropped by the throw; "good" still emitted; crawl finished.
        Assert.Equal(new[] { "good" }, sink.Emitted.Select(p => p.Url));
    }

    [Fact]
    public async Task Subscribe_delegate_sink_forwards_each_record_to_the_handler()
    {
        // ADR-0038: ScraperEngineBuilder.Subscribe folds into a DelegateSink —
        // a delegate destination on the Sink seam, not a separate notification.
        var received = new List<string>();
        var sink = new DelegateSink(d => { lock (received) received.Add(d.Url); });

        var spider = new ScriptedSpider(job =>
            job.Url == "root" ? Followed("item") : Parsed(job.Url));

        var engine = new ScraperEngine(
            parallelismDegree: 1,
            new FakeConfigStorage(Config()),
            new InMemoryScheduler(),
            spider,
            new InMemoryVisitedLinkTracker(),
            new List<IScraperSink> { sink },
            NullLogger.Instance);

        await engine.RunAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(new[] { "item" }, received);
    }

    [Fact]
    public async Task Run_terminates_over_a_durable_style_scheduler_that_drains()
    {
        // ADR-0037: termination no longer depends on IScheduler.Complete().
        // PollingScheduler's stream ends only when its token is cancelled — the
        // shape of every durable scheduler. The crawl drains (root + 2
        // children); the stop rule concludes and the driver cancels its own
        // consumption. Before ADR-0037 this hung forever (Complete() was a
        // no-op for every durable scheduler) — WaitAsync would time out.
        var spider = new ScriptedSpider(job =>
            job.Url == "root" ? Followed("child-A", "child-B") : Followed());

        var engine = new ScraperEngine(
            parallelismDegree: 4,
            new FakeConfigStorage(Config()),
            new PollingScheduler(),
            spider,
            new InMemoryVisitedLinkTracker(),
            new List<IScraperSink>(),
            NullLogger.Instance);

        await engine.RunAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(3, spider.Crawled.Count); // root + 2 children, then drained
    }

    [Fact]
    public async Task Run_terminates_over_a_durable_style_scheduler_when_the_page_limit_is_hit()
    {
        // ADR-0037: the cutoff (soft page limit) path also ends the crawl by
        // the driver ceasing consumption, so it too terminates over a durable
        // scheduler. The spider follows an endless chain; the limit of 3
        // visited pages concludes the crawl.
        var spider = new ScriptedSpider(job => Followed(job.Url + "-x"));

        var engine = new ScraperEngine(
            parallelismDegree: 1,
            new FakeConfigStorage(Config(limit: 3, stopWhenDrained: false)),
            new PollingScheduler(),
            spider,
            new InMemoryVisitedLinkTracker(),
            new List<IScraperSink>(),
            NullLogger.Instance);

        await engine.RunAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(spider.Crawled.Count >= 3); // soft limit: at least the cap
    }
}
