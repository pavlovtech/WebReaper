using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.ConfigStorage.Abstract;
using WebReaper.Core;
using WebReaper.Core.Crawling;
using WebReaper.Core.LinkTracker.Concrete;
using WebReaper.Core.Scheduler.Concrete;
using WebReaper.Core.Spider.Abstract;
using WebReaper.Domain;
using WebReaper.Domain.Selectors;
using WebReaper.Infra.Abstract;
using WebReaper.Sinks.Abstract;
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
    public async Task Sink_fan_out_and_callbacks_fire_only_for_target_pages()
    {
        var sink = new RecordingSink();
        var scraped = new List<ParsedData>();
        var postProcessed = new List<string>();

        var spider = new ScriptedSpider(job =>
            job.Url == "root" ? Followed("item") : Parsed(job.Url));

        var engine = new ScraperEngine(
            parallelismDegree: 1,
            new FakeConfigStorage(Config()),
            new InMemoryScheduler(),
            spider,
            new InMemoryVisitedLinkTracker(),
            new List<IScraperSink> { sink },
            NullLogger.Instance,
            scrapedData: d => { lock (scraped) scraped.Add(d); },
            postProcessor: (m, _) => { lock (postProcessed) postProcessed.Add(m.Url); return Task.CompletedTask; });

        await engine.RunAsync().WaitAsync(TimeSpan.FromSeconds(10));

        // "root" is Followed -> no emission/notification; only "item" is Parsed.
        Assert.Equal(new[] { "item" }, sink.Emitted.Select(p => p.Url));
        Assert.Equal(new[] { "item" }, scraped.Select(p => p.Url));
        Assert.Equal(new[] { "item" }, postProcessed);
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
}
