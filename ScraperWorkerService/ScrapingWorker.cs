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

        var blackList = new string[] {
            "https://rutracker.org/forum/viewforum.php?f=396",
            "https://rutracker.org/forum/viewforum.php?f=2322",
            "https://rutracker.org/forum/viewforum.php?f=1993",
            "https://rutracker.org/forum/viewforum.php?f=2167",
            "https://rutracker.org/forum/viewforum.php?f=2321"
        };

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
            .IgnoreUrls(blackList)
            .WithSpiders(4)
            .To("output.json");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await scraper.Run();
    }
}

