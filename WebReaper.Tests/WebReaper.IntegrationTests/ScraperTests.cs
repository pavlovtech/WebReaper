using System.Reflection;
using WebReaper.ProxyProviders.WebShareProxy;
using Xunit.Abstractions;
using PuppeteerSharp;
using WebReaper.Builders;
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
                "https://www.reddit.com/r/dotnet/",
                "https://www.reddit.com/r/worldnews/",
                "https://www.reddit.com/r/ukraine/"
            };
            
            var engine = await new ScraperEngineBuilder()
                .Get(startUrls)
                .Follow("a.SQnoC3ObvgnGjWt90zD9Z._2INHSNB8V5eaWp4P0rY_mE")
                .Parse(new()
                {
                    new("title", "._eYtD2XCVieq6emjKBH3m"),
                    new("text", "._3xX726aBn29LDbsDtzr_6E._1Ap4F5maDtT1E1YuCiaO0r.D3IL3FD0RFy_mkKLPwL4")
                })
                .WithLogger(new TestOutputLogger(this.output))
                .Subscribe(x => result.Add(x))
                .BuildAsync();

            _ = engine.RunAsync();

            await Task.Delay(15000);

            Assert.NotEmpty(result);
            Assert.True(result.Any(r => r.Url.StartsWith(startUrls[0])));
            Assert.True(result.Any(r => r.Url.StartsWith(startUrls[1])));
            Assert.True(result.Any(r => r.Url.StartsWith(startUrls[2])));
        }
        
        [Fact]
        public async Task SimpleTest()
        {
            var result = new List<ParsedData>();

            var engine = await new ScraperEngineBuilder()
                .Get("https://www.reddit.com/r/dotnet/")
                .Follow("a.SQnoC3ObvgnGjWt90zD9Z._2INHSNB8V5eaWp4P0rY_mE")
                .Parse(new()
                {
                    new("title", "._eYtD2XCVieq6emjKBH3m"),
                    new("text", "._3xX726aBn29LDbsDtzr_6E._1Ap4F5maDtT1E1YuCiaO0r.D3IL3FD0RFy_mkKLPwL4")
                })
                .WithLogger(new TestOutputLogger(output))
                .Subscribe(x => result.Add(x))
                .WithParallelismDegree(1)
                .BuildAsync();

            _ = engine.RunAsync();

            await Task.Delay(10000);

            Assert.NotEmpty(result);
        }

        [Fact (Skip = "No stable proxy at the moment")]
        public async Task SimpleTestWithProxy()
        {
            var result = new List<ParsedData>();

            var scraper = await new ScraperEngineBuilder()
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

            await browserFetcher.DownloadAsync(BrowserFetcher.DefaultChromiumRevision);

            var result = new List<ParsedData>();

            var engine = await new ScraperEngineBuilder()
                .GetWithBrowser(new []{"https://www.reddit.com/r/dotnet/"})
                .FollowWithBrowser("a.SQnoC3ObvgnGjWt90zD9Z._2INHSNB8V5eaWp4P0rY_mE")
                .Parse(new()
                {
                    new("title", "._eYtD2XCVieq6emjKBH3m"),
                    new("text", "._3xX726aBn29LDbsDtzr_6E._1Ap4F5maDtT1E1YuCiaO0r.D3IL3FD0RFy_mkKLPwL4")
                })
                .WithLogger(new TestOutputLogger(this.output))
                .Subscribe(x => result.Add(x))
                .WithParallelismDegree(10)
                .BuildAsync();

            _ = engine.RunAsync();

            await Task.Delay(20000);

            Assert.NotEmpty(result);
        }
    }
}