using WebReaper.Core.DOM;
using WebReaper.Core.Scraper;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;

namespace ScraperWorkerService;

public class ScrapingWorker : BackgroundService
{
    private Scraper scraper;

    public ScrapingWorker(ILogger<ScrapingWorker> logger)
    {
        var blackList = new[] {
            "https://rutracker.org/forum/viewforum.php?f=396",
            "https://rutracker.org/forum/viewforum.php?f=2322",
            "https://rutracker.org/forum/viewforum.php?f=1993",
            "https://rutracker.org/forum/viewforum.php?f=2167",
            "https://rutracker.org/forum/viewforum.php?f=2321"
        };

        scraper = new Scraper()
            .WithLogger(logger)
            .WithStartUrl("https://rutracker.org/forum/index.php?c=33", PageType.Dynamic)
            .FollowLinks("#cf-33 .forumlink>a", PageType.Dynamic)
            .FollowLinks(".forumlink>a", PageType.Dynamic)
            .FollowLinks("a.torTopic", ".pg", PageType.Dynamic)
            .Parse(new Schema {
                new("name", "#topic-title"),
                new("category", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(3)"),
                new("subcategory", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(5)"),
                new("torrentSize", "div.attach_link.guest>ul>li:nth-child(2)"),
                new Url("torrentLink", ".magnet-link"),
                new Image("coverImageUrl", ".postImg")
            })
            .WriteToJsonFile("result.json")
            .WriteToCsvFile("result.csv")
            .IgnoreUrls(blackList);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await scraper.Run(1);
    }
}

