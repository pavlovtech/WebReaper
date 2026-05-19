using System.Reflection;
using WebReaper.ProxyProviders.WebShareProxy;
using Xunit.Abstractions;
using PuppeteerSharp;
using WebReaper.Builders;
using WebReaper.Puppeteer;
using WebReaper.Sinks.Models;

namespace WebReaper.IntegrationTests
{
    public class ScraperEngineTests
    {
        private readonly ITestOutputHelper output;

        public ScraperEngineTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public async Task StartScrapingWithMultipleStartUrls()
        {
            var result = new List<ParsedData>();

            var startUrls = new[]
            {
                "https://www.alexpavlov.dev/blog/tags/csharp",
                "https://www.alexpavlov.dev/blog/tags/ukraine",
                "https://www.alexpavlov.dev/blog/tags/web"
            };
            
            var engine = await ScraperEngineBuilder
                .Crawl(startUrls)
                .Extract(new()
                {
                    new("title", ".text-3xl.font-bold"),
                    new("text", ".max-w-max.prose.prose-dark")
                })
                .Follow(".text-gray-900.transition")
                .WithLogger(new TestOutputLogger(this.output))
                .Subscribe(x => result.Add(x))
                .BuildAsync();

            _ = engine.RunAsync();

            await Task.Delay(25000);

            Assert.NotEmpty(result);
            Assert.True(result.Count > 1);
        }
        
        [Fact]
        public async Task SimpleTest()
        {
            var result = new List<ParsedData>();

            var engine = await ScraperEngineBuilder
                .Crawl("https://www.alexpavlov.dev/blog")
                .Extract(new()
                {
                    new("title", ".text-3xl.font-bold"),
                    new("text", ".max-w-max.prose.prose-dark")
                })
                .Follow(".text-gray-900.transition")
                .WithLogger(new TestOutputLogger(output))
                .Subscribe(result.Add)
                .WithParallelismDegree(1)
                .BuildAsync();

            _ = engine.RunAsync();

            await Task.Delay(15000);

            Assert.NotEmpty(result);
            Assert.True(result.Count > 1);
        }

        [Fact (Skip = "No stable proxy at the moment")]
        public async Task SimpleTestWithProxy()
        {
            var result = new List<ParsedData>();

            var scraper = await ScraperEngineBuilder
                .Crawl("https://www.reddit.com/r/dotnet/")
                .Extract(new()
                {
                    new("title", "._eYtD2XCVieq6emjKBH3m"),
                    new("text", "._3xX726aBn29LDbsDtzr_6E._1Ap4F5maDtT1E1YuCiaO0r.D3IL3FD0RFy_mkKLPwL4")
                })
                .Follow("a.SQnoC3ObvgnGjWt90zD9Z._2INHSNB8V5eaWp4P0rY_mE")
                .WithLogger(new TestOutputLogger(this.output))
                .WithProxies(new WebShareProxyProvider())
                .Subscribe(x => result.Add(x))
                .WithParallelismDegree(2)
                .BuildAsync();

            _ = scraper.RunAsync();

            await Task.Delay(30000);

            Assert.NotEmpty(result);
        }

        [Fact]
        public async Task SimpleTestWithSPA()
        {
            var browserFetcher = new BrowserFetcher(new BrowserFetcherOptions
            {
                Path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            });

            await browserFetcher.DownloadAsync();

            var result = new List<ParsedData>();

            var engine = await ScraperEngineBuilder
                .CrawlWithBrowser(new []{ "https://www.alexpavlov.dev/blog" })
                .Extract(new()
                {
                    new("title", ".text-3xl.font-bold"),
                    new("text", ".max-w-max.prose.prose-dark")
                })
                .WithPuppeteerPageLoader()
                .FollowWithBrowser(".text-gray-900.transition")
                .WithLogger(new TestOutputLogger(this.output))
                .Subscribe(x => result.Add(x))
                .WithParallelismDegree(10)
                .BuildAsync();

            _ = engine.RunAsync();

            await Task.Delay(40000);

            Assert.NotEmpty(result);
        }
    }
}