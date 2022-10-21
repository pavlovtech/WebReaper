using WebReaper.Core;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;

namespace BrownsfashionScraper
{
    public class ScrapingWorker : BackgroundService
    {
        private Scraper scraper;

        private readonly ILogger<ScrapingWorker> _logger;

        public ScrapingWorker(ILogger<ScrapingWorker> logger)
        {
            _logger = logger;

            scraper = new Scraper("Brownsfashion")
            .WithLogger(logger)
            .WithStartUrl("https://www.brownsfashion.com/ua/shopping/man-clothing", PageType.Dynamic)
            .FollowLinks("._1GX2o>a", ".AlEkI", PageType.Dynamic)
            .Parse(new Schema {
                new("brand", "a[data-test=\"product-brand\"]"),
                new("product", "a[data-test=\"product-name\"]"),
                new("price", "a[data-test=\"product-price\"]"),
            })
            .WriteToJsonFile("result.json");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await scraper.Run(1);
        }
    }
}