using System.Collections.Concurrent;
using WebReaper.ProxyProviders.WebShareProxy;
using Xunit;
using Xunit.Abstractions;
using WebReaper.Builders;
using WebReaper.Playwright;
using WebReaper.Sinks.Models;

namespace WebReaper.IntegrationTests
{
    // [Trait LiveSite]: real-internet crawls against alexpavlov.dev — flaky by
    // nature (network + the live site's markup), so they stay OUT of the CI
    // gate (which runs Category=LocalServer|Cli only). Converted from the old
    // `_ = RunAsync(); await Task.Delay(N)` sampling to bounded await-completion
    // (PageCrawlLimit + StopWhenAllLinksProcessed + `await RunAsync`) so they
    // finish deterministically instead of hoping a fixed delay was long enough.
    // A 60s safety token keeps a hung crawl from wedging the run.
    [Trait("Category", "LiveSite")]
    public class ScraperEngineTests
    {
        private readonly ITestOutputHelper output;

        public ScraperEngineTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static CancellationToken Safety() =>
            new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token;

        [Fact]
        public async Task StartScrapingWithMultipleStartUrls()
        {
            var result = new ConcurrentQueue<ParsedData>();

            var startUrls = new[]
            {
                "https://www.alexpavlov.dev/blog/tags/csharp",
                "https://www.alexpavlov.dev/blog/tags/ukraine",
                "https://www.alexpavlov.dev/blog/tags/web"
            };

            await using (var engine = await ScraperEngineBuilder
                .Crawl(startUrls)
                .Extract(new()
                {
                    new("title", ".text-3xl.font-bold"),
                    new("text", ".max-w-max.prose.prose-dark")
                })
                .Follow(".text-gray-900.transition")
                .WithLogger(new TestOutputLogger(this.output))
                .Subscribe(result.Enqueue)
                .PageCrawlLimit(10)
                .StopWhenAllLinksProcessed()
                .BuildAsync())
            {
                await engine.RunAsync(Safety());
            }

            Assert.NotEmpty(result);
            Assert.True(result.Count > 1);
        }

        [Fact]
        public async Task SimpleTest()
        {
            var result = new ConcurrentQueue<ParsedData>();

            await using (var engine = await ScraperEngineBuilder
                .Crawl("https://www.alexpavlov.dev/blog")
                .Extract(new()
                {
                    new("title", ".text-3xl.font-bold"),
                    new("text", ".max-w-max.prose.prose-dark")
                })
                .Follow(".text-gray-900.transition")
                .WithLogger(new TestOutputLogger(output))
                .Subscribe(result.Enqueue)
                .PageCrawlLimit(10)
                .StopWhenAllLinksProcessed()
                .BuildAsync())
            {
                await engine.RunAsync(Safety());
            }

            Assert.NotEmpty(result);
            Assert.True(result.Count > 1);
        }

        [Fact(Skip = "No stable proxy at the moment")]
        public async Task SimpleTestWithProxy()
        {
            var result = new ConcurrentQueue<ParsedData>();

            await using (var scraper = await ScraperEngineBuilder
                .Crawl("https://www.reddit.com/r/dotnet/")
                .Extract(new()
                {
                    new("title", "._eYtD2XCVieq6emjKBH3m"),
                    new("text", "._3xX726aBn29LDbsDtzr_6E._1Ap4F5maDtT1E1YuCiaO0r.D3IL3FD0RFy_mkKLPwL4")
                })
                .Follow("a.SQnoC3ObvgnGjWt90zD9Z._2INHSNB8V5eaWp4P0rY_mE")
                .WithLogger(new TestOutputLogger(this.output))
                .WithProxies(new WebShareProxyProvider())
                .PageCrawlLimit(10)
                .StopWhenAllLinksProcessed()
                .Subscribe(result.Enqueue)
                .BuildAsync())
            {
                await scraper.RunAsync(Safety());
            }

            Assert.NotEmpty(result);
        }

        [Fact]
        public async Task SimpleTestWithSPA()
        {
            // ADR-0053: WebReaper.Playwright manages browser binaries via the
            // standard `playwright install` step (auto-runs on first use); the
            // pre-v10 PuppeteerSharp BrowserFetcher block is gone.
            var result = new ConcurrentQueue<ParsedData>();

            await using (var engine = await ScraperEngineBuilder
                .CrawlWithBrowser(new[] { "https://www.alexpavlov.dev/blog" })
                .Extract(new()
                {
                    new("title", ".text-3xl.font-bold"),
                    new("text", ".max-w-max.prose.prose-dark")
                })
                .WithPlaywrightPageLoader()
                .FollowWithBrowser(".text-gray-900.transition")
                .WithLogger(new TestOutputLogger(this.output))
                .Subscribe(result.Enqueue)
                .PageCrawlLimit(10)
                .StopWhenAllLinksProcessed()
                .BuildAsync())
            {
                await engine.RunAsync(Safety());
            }

            Assert.NotEmpty(result);
        }
    }
}
