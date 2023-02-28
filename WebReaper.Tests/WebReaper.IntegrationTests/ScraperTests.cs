using System.Reflection;
using WebReaper.ProxyProviders.WebShareProxy;
using Xunit.Abstractions;
using PuppeteerSharp;
using WebReaper.Core.Builders;
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
        public async Task SimpleTest()
        {
            var result = new List<ParsedData>();

            var engine = new ScraperEngineBuilder()
                .Get("https://www.reddit.com/r/dotnet/")
                .Follow("a.SQnoC3ObvgnGjWt90zD9Z._2INHSNB8V5eaWp4P0rY_mE")
                .Parse(new()
                {
                    new("title", "._eYtD2XCVieq6emjKBH3m"),
                    new("text", "._3xX726aBn29LDbsDtzr_6E._1Ap4F5maDtT1E1YuCiaO0r.D3IL3FD0RFy_mkKLPwL4")
                })
                .WithLogger(new TestOutputLogger(this.output))
                .Subscribe(x => result.Add(x))
                .Build();

            _ = engine.Run(2);

            await Task.Delay(10000);

            Assert.NotEmpty(result);
        }

        [Fact (Skip = "No stable proxy at the moment")]
        public async Task SimpleTestWithProxy()
        {
            var result = new List<ParsedData>();

            var scraper = new ScraperEngineBuilder()
                .Get("https://www.reddit.com/r/dotnet/")
                .Follow("a.SQnoC3ObvgnGjWt90zD9Z._2INHSNB8V5eaWp4P0rY_mE")
                .Parse(new()
                {
                    new("title", "._eYtD2XCVieq6emjKBH3m"),
                    new("text", "._3xX726aBn29LDbsDtzr_6E._1Ap4F5maDtT1E1YuCiaO0r.D3IL3FD0RFy_mkKLPwL4")
                })
                .WithLogger(new TestOutputLogger(this.output))
                .WithProxies(new WebShareProxyProvider())
                .Subscribe(x => result.Add(x))
                .Build();

            _ = scraper.Run(2);

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

            await browserFetcher.DownloadAsync(BrowserFetcher.DefaultChromiumRevision);

            var result = new List<ParsedData>();

            var engine = new ScraperEngineBuilder()
                .GetWithBrowser("https://www.reddit.com/r/dotnet/")
                .FollowWithBrowser("a.SQnoC3ObvgnGjWt90zD9Z._2INHSNB8V5eaWp4P0rY_mE")
                .Parse(new()
                {
                    new("title", "._eYtD2XCVieq6emjKBH3m"),
                    new("text", "._3xX726aBn29LDbsDtzr_6E._1Ap4F5maDtT1E1YuCiaO0r.D3IL3FD0RFy_mkKLPwL4")
                })
                .WithLogger(new TestOutputLogger(this.output))
                .Subscribe(x => result.Add(x))
                .Build();

            _ = engine.Run(10);

            await Task.Delay(20000);

            Assert.NotEmpty(result);
        }
    }
}