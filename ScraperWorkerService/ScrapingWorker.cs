using WebReaper.Domain;
using WebReaper.Scraper.Abstract;
using WebReaper.Scraper.Concrete;

namespace ScraperWorkerService;

public class ScrapingWorker : BackgroundService
{
    private readonly ILogger<ScrapingWorker> _logger;

    private IScraper scraper;
    public ScrapingWorker(ILogger<ScrapingWorker> logger)
    {
        _logger = logger;

        scraper = new Scraper(logger)
            .WithStartUrl("https://rutracker.org/forum/index.php?c=33")
            .FollowLinks("#cf-33 .forumlink>a")
            .FollowLinks(".forumlink>a")
            .FollowLinks("a.torTopic")
            .Paginate(".pg")
            .WithScheme(new WebEl[] {
                new("title", "title"),
                new("name", ".post_body>span"),
            })
            .WithSpiders(2)
            .To("output.json");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await scraper.Run();
    }
}

