using WebReaper.Core;
using WebReaper.Core.Builders;

namespace WebReaper.ScraperWorkerService;

public class ScrapingWorker : BackgroundService
{
    private ScraperEngine engine;

    public ScrapingWorker(ILogger<ScrapingWorker> logger)
    {
        var blackList = new[] {
            "https://rutracker.org/forum/viewforum.php?f=396",
            "https://rutracker.org/forum/viewforum.php?f=2322",
            "https://rutracker.org/forum/viewforum.php?f=1993",
            "https://rutracker.org/forum/viewforum.php?f=2167",
            "https://rutracker.org/forum/viewforum.php?f=2321"
        };

        engine = new EngineBuilder("rutracker")
            .WithLogger(logger)
            .Get("https://rutracker.org/forum/index.php?c=33")
            .Follow("#cf-33 .forumlink>a")
            .Follow(".forumlink>a")
            .Paginate("a.torTopic", ".pg")
            .Parse(new()
            {
                new("name", "#topic-title"),
                new("category", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(3)"),
                new("subcategory", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(5)"),
                new("torrentSize", "div.attach_link.guest>ul>li:nth-child(2)"),
                new("torrentLink", ".magnet-link", "href"),
                new("coverImageUrl", ".postImg", "src")
            })
            .WriteToJsonFile("result.json")
            .IgnoreUrls(blackList)
            .ScheduleWithTextFile("jobs.txt", "progress.txt")
            .TrackVisitedLinksInFile("links.txt")
            .Build();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await engine.Run(10, stoppingToken);
    }
}

