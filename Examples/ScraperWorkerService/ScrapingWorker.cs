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
        var blackList = new string[] {
            "https://rutracker.org/forum/viewforum.php?f=396",
            "https://rutracker.org/forum/viewforum.php?f=2322",
            "https://rutracker.org/forum/viewforum.php?f=1993",
            "https://rutracker.org/forum/viewforum.php?f=2167",
            "https://rutracker.org/forum/viewforum.php?f=2321"
        };

        scraper = new Scraper()
            .WithLogger(logger)
            .WithStartUrl("https://rutracker.org/forum/index.php?c=33")
            .FollowLinks("#cf-33 .forumlink>a")
            .FollowLinks(".forumlink>a")
            .FollowLinks("a.torTopic", ".pg")
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

        /* SPA scrapping example */
        //scraper = new Scraper()
        //   .WithLogger(logger)
        //   .WithStartUrl("https://rutracker.org/forum/index.php?c=33")
        //   .FollowSPALinks("#cf-33 .forumlink>a", pageType: PageType.SPA)
        //   .FollowSPALinks(".forumlink>a", pageType: PageType.SPA)
        //   .FollowSPALinks("a.torTopic", ".pg", pageType: PageType.SPA)
        //   .Parse(new Schema {
        //        new("name", "#topic-title"),
        //        new("category", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(3)"),
        //        new("subcategory", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(5)"),
        //        new("torrentSize", "div.attach_link.guest>ul>li:nth-child(2)"),
        //        new Url("torrentLink", ".magnet-link"),
        //        new Image("coverImageUrl", ".postImg")
        //   })
        //   .WriteToJsonFile("result.json")
        //   .WriteToCsvFile("result.csv")
        //   .IgnoreUrls(blackList);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await scraper.Run(100);
    }
}

