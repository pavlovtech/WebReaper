using WebReaper.Core;
using WebReaper.Core.Builders;
using WebReaper.Domain.Parsing;
using WebReaper.LinkTracker.Concrete;
using WebReaper.Scheduler.Concrete;

namespace WebReaper.DistributedScraperWorkerService;

public class ScrapingWorker : BackgroundService
{
    private readonly ScraperEngine engine;

    public ScrapingWorker(ILogger<ScrapingWorker> logger)
    {
        var blackList = new string[] {
            "https://rutracker.org/forum/viewforum.php?f=396",
            "https://rutracker.org/forum/viewforum.php?f=2322",
            "https://rutracker.org/forum/viewforum.php?f=1993",
            "https://rutracker.org/forum/viewforum.php?f=2167",
            "https://rutracker.org/forum/viewforum.php?f=2321"
        };

        var redisConnectionString = "webreaper.redis.cache.windows.net:6380,password=etUgOS0XUTTpZqNGlSlmaczrDKTeySPBWAzCaAMhsVU=,ssl=True,abortConnect=False";
        var azureSBConnectionString = "Endpoint=sb://webreaper.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=g0AAACe/NXS+/qWVad4KUnnw6iGECmUTJTpfFOMfjms=";
        var queue = "jobqueue";

        engine = new EngineBuilder("rutracker")
            .WithLogger(logger)
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
            .TrackVisitedLinksInRedis(redisConnectionString)
            .WriteToCosmosDb(
                "https://webreaperdbserverless.documents.azure.com:443/",
                "TssEjPIdgShphVKhFkxrAu6WJovPdIZLTFNshJWGdXuitWPIMlXTidc05WFqm20qFVz8leE8zc5JBOphlNmRYg==",
                "DistributedWebReaper",
                "Rutracker")
            .WithScheduler(new AzureServiceBusScheduler(azureSBConnectionString, queue))
            .Build();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await engine.Run(10, stoppingToken);
    }
}

