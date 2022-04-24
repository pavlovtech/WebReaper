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
            .WithScheme(new SchemaElement[] {
                new("OriginUrl", ContentType.PageUrl),
                new("coverImageUrl", ".postImg", ContentType.Image),
                new("name", "#topic-title"),
                new("category", ".t-breadcrumb-top>a:nth-child(3)"),
                new("subcategory", ".t-breadcrumb-top>a:nth-child(4)"),
                new("torrentSize", "div.attach_link.guest>ul>li:nth-child(2)"),
                new("TorrentLink", ".magnet-link"),
                //new("Seeds", ".post_body>span"),
                //new("Leeches", ".post_body>span"),
                //new("Downloads", ".post_body>span"),
                //new("Replays", ".post_body>span")
            })
            .IgnoreUrls(blackList)
            .WithParallelismDegree(1)
            .To("output.json");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await scraper.Run();
    }
}

