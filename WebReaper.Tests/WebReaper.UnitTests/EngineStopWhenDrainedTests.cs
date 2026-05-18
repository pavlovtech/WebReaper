using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.ConfigStorage.Abstract;
using WebReaper.Core;
using WebReaper.Core.Crawling;
using WebReaper.Core.LinkTracker.Concrete;
using WebReaper.Core.Scheduler.Concrete;
using WebReaper.Core.Spider.Abstract;
using WebReaper.Domain;
using WebReaper.Domain.Selectors;
using WebReaper.Sinks.Abstract;

namespace WebReaper.UnitTests
{
    public class EngineStopWhenDrainedTests
    {
        private sealed class FakeConfigStorage : IScraperConfigStorage
        {
            private readonly ScraperConfig _config;
            public FakeConfigStorage(ScraperConfig config) => _config = config;
            public Task CreateConfigAsync(ScraperConfig config) => Task.CompletedTask;
            public Task<ScraperConfig> GetConfigAsync() => Task.FromResult(_config);
        }

        // root -> 2 children -> each 0 children. Finite tree: 3 crawls.
        private sealed class FiniteSpider : ISpider
        {
            public int Crawls;

            public Task<JobReport> CrawlAsync(Job job, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref Crawls);

                var children = job.Url == "root"
                    ? ImmutableArray.Create(
                        new Job("child-1", ImmutableQueue<LinkPathSelector>.Empty, ImmutableQueue<string>.Empty),
                        new Job("child-2", ImmutableQueue<LinkPathSelector>.Empty, ImmutableQueue<string>.Empty))
                    : ImmutableArray<Job>.Empty;

                return Task.FromResult(new JobReport(CrawlOutcome.Transit(children), string.Empty));
            }
        }

        private static ScraperConfig Config(bool stopWhenDrained) => new(
            ParsingScheme: null,
            LinkPathSelectors: ImmutableQueue<LinkPathSelector>.Empty,
            StartUrls: new[] { "root" },
            UrlBlackList: Array.Empty<string>(),
            StopWhenDrained: stopWhenDrained);

        [Fact]
        public async Task RunAsyncCompletesWhenDrained()
        {
            var spider = new FiniteSpider();
            var engine = new ScraperEngine(
                parallelismDegree: 4,
                new FakeConfigStorage(Config(stopWhenDrained: true)),
                new InMemoryScheduler(),
                spider,
                new InMemoryVisitedLinkTracker(),
                new List<IScraperSink>(),
                NullLogger.Instance);

            // Throws TimeoutException (fails the test) if it never stops.
            await engine.RunAsync().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(3, spider.Crawls); // root + 2 children
        }

        [Fact]
        public async Task RunAsyncRunsForeverWithoutTheOption()
        {
            var engine = new ScraperEngine(
                parallelismDegree: 4,
                new FakeConfigStorage(Config(stopWhenDrained: false)),
                new InMemoryScheduler(),
                new FiniteSpider(),
                new InMemoryVisitedLinkTracker(),
                new List<IScraperSink>(),
                NullLogger.Instance);

            using var cts = new CancellationTokenSource();
            var run = engine.RunAsync(cts.Token);

            var finishedFirst = await Task.WhenAny(run, Task.Delay(1000)) == run;

            // Default behavior unchanged: the engine keeps waiting for jobs.
            Assert.False(finishedFirst);

            cts.Cancel();
            try { await run; } catch { /* expected cancellation */ }
        }
    }
}
