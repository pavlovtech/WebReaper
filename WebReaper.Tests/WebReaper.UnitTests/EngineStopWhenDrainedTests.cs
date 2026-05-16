using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.ConfigStorage.Abstract;
using WebReaper.Core;
using WebReaper.Core.Scheduler.Concrete;
using WebReaper.Core.Spider.Abstract;
using WebReaper.Domain;
using WebReaper.Domain.Selectors;

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

            public Task<List<Job>> CrawlAsync(Job job, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref Crawls);

                var children = job.Url == "root"
                    ? new List<Job>
                    {
                        new("child-1", ImmutableQueue<LinkPathSelector>.Empty, ImmutableQueue<string>.Empty),
                        new("child-2", ImmutableQueue<LinkPathSelector>.Empty, ImmutableQueue<string>.Empty)
                    }
                    : new List<Job>();

                return Task.FromResult(children);
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
