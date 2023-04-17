using WebReaper.Builders;
using WebReaper.Core;
using WebReaper.Domain.Parsing;

namespace WebReaper.DistributedScraperWorkerService;

public class ScrapingWorker : BackgroundService
{
    private readonly ILogger<ScrapingWorker> _logger;

    public ScrapingWorker(ILogger<ScrapingWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var blackList = new string[] {
            "https://rutracker.org/forum/viewforum.php?f=396",
            "https://rutracker.org/forum/viewforum.php?f=2322",
            "https://rutracker.org/forum/viewforum.php?f=1993",
            "https://rutracker.org/forum/viewforum.php?f=2167",
            "https://rutracker.org/forum/viewforum.php?f=2321"
        };

        var redisConnectionString = "";
        var azureSBConnectionString = "";
        var queue = "jobqueue";

        var engine = await new ScraperEngineBuilder()
            .WithLogger(_logger)
            .Get("https://rutracker.org/forum/index.php?c=33")
            .IgnoreUrls(blackList)
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
            .TrackVisitedLinksInRedis(redisConnectionString, "rutracker-visited-links")
            .WriteToCosmosDb(
                "",
                "TssEjPIdgShphVKhFkxrAu6WJovPdIZLTFNshJWGdXuitWPIMlXTidc05WFqm20qFVz8leE8zc5JBOphlNmRYg==",
                "DistributedScraper",
                "Rutracker",
                true)
            .WithAzureServiceBusScheduler(azureSBConnectionString, queue)
            .WithParallelismDegree(10)
            .BuildAsync();
        
        await engine.RunAsync(stoppingToken);
    }
}

