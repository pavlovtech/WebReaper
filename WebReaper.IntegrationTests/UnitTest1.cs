using WebReaper.Core.DOM;
using WebReaper.Core.Scraper;
using WebReaper.Domain.Parsing;

namespace WebReaper.IntegrationTests
{
    public class ScraperTests
    {
        [Fact]
        public async Task ListTest()
        {
            var scraper = new Scraper()
                .WithStartUrl("https://kniga.io/books/pelevin-viktor-snuff0")
                .Parse(new Schema {
                    new ElementList("genres", ".badge.bg-success.rounded-pill"),
                })
                .WriteToConsole();

            await scraper.Run(1);

            await scraper.Stop();
        }
    }
}