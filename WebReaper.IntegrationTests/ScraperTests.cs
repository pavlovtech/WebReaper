using Newtonsoft.Json.Linq;
using WebReaper.ConsoleApplication;
using WebReaper.Core;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;
using Xunit.Abstractions;

namespace WebReaper.IntegrationTests
{
    public class ScraperTests
    {
        private readonly ITestOutputHelper output;

        public ScraperTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public async Task SimpleTest()
        {
            List<JObject> result = new List<JObject>();

            var scraper = new Scraper("reddit")
                .WithStartUrl("https://www.reddit.com/r/dotnet/")
                .FollowLinks("a.SQnoC3ObvgnGjWt90zD9Z._2INHSNB8V5eaWp4P0rY_mE")
                .Parse(new Schema
                {
                    new("title", "._eYtD2XCVieq6emjKBH3m"),
                    new("text", "._3xX726aBn29LDbsDtzr_6E._1Ap4F5maDtT1E1YuCiaO0r.D3IL3FD0RFy_mkKLPwL4")
                })
                .WithLogger(new TestOutputLogger(this.output))
                .AddScrapedDataHandler(x => result.Add(x));

            _ = scraper.Run(1);

            await Task.Delay(10000);

            Assert.NotEmpty(result);
        }

        [Fact]
        public async Task SimpleTestWithSPA()
        {
            List<JObject> result = new List<JObject>();

            var scraper = new Scraper("reddit")
                .WithStartUrl("https://www.reddit.com/r/dotnet/", PageType.Dynamic)
                .FollowLinks("a.SQnoC3ObvgnGjWt90zD9Z._2INHSNB8V5eaWp4P0rY_mE", PageType.Dynamic)
                .Parse(new Schema
                {
                    new("title", "._eYtD2XCVieq6emjKBH3m"),
                    new("text", "._3xX726aBn29LDbsDtzr_6E._1Ap4F5maDtT1E1YuCiaO0r.D3IL3FD0RFy_mkKLPwL4")
                })
                .WithLogger(new TestOutputLogger(this.output))
                .AddScrapedDataHandler(x => result.Add(x));

            _ = scraper.Run(1);

            await Task.Delay(20000);

            Assert.NotEmpty(result);
        }
    }
}