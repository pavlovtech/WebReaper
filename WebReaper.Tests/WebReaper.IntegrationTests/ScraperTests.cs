using Newtonsoft.Json.Linq;
using WebReaper.ProxyProviders.WebShareProxy;
using Xunit.Abstractions;
using PuppeteerSharp;
using WebReaper.Core.Builders;

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
            List<JObject> result = new List<JObject>();

            var engine = new ScraperEngineBuilder("reddit")
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

            _ = engine.Run(1);

            await Task.Delay(10000);

            Assert.NotEmpty(result);
        }

        [Fact]
        public async Task SimpleTestWithProxy()
        {
            List<JObject> result = new List<JObject>();

            var scraper = new ScraperEngineBuilder("reddit")
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

            _ = scraper.Run(1);

            await Task.Delay(20000);

            Assert.NotEmpty(result);
        }

        [Fact]
        public async Task SimpleTestWithSPA()
        {
            var browserFetcher = new BrowserFetcher(new BrowserFetcherOptions
            {
                Path = Path.GetTempPath()
            });

            await browserFetcher.DownloadAsync(BrowserFetcher.DefaultChromiumRevision);

            List<JObject> result = new List<JObject>();

            var engine = new ScraperEngineBuilder("reddit")
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