using WebReaper.Domain.Parsing;
using WebReaper.LinkTracker;
using WebReaper.Parsing;
using WebReaper.Queue.AzureServiceBus;
using WebReaper.Scraper;

namespace DistributedScraperWorkerService;

public class ScrapingWorker : BackgroundService
{
    private readonly ScraperRunner runner;

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

        var config = new ScraperConfigBuilder()
            .WithLogger(logger)
            .WithStartUrl("https://rutracker.org/forum/index.php?c=33")
            .FollowLinks("#cf-33 .forumlink>a")
            .FollowLinks(".forumlink>a")
            .FollowLinks("a.torTopic", ".pg")
            .WithScheme(new Schema {
                new("name", "#topic-title"),
                new("category", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(3)"),
                new("subcategory", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(5)"),
                new("torrentSize", "div.attach_link.guest>ul>li:nth-child(2)"),
                new Url("torrentLink", ".magnet-link"),
                new Image("coverImageUrl", ".postImg")
            })
            .Build();

        var spider = new SpiderBuilder()
            .WithLinkTracker(new RedisCrawledLinkTracker(redisConnectionString))
            .WithJobQueueReader(new AzureJobQueueReader(azureSBConnectionString, queue))
            .WithJobQueueWriter(new AzureJobQueueWriter(azureSBConnectionString, queue))
            .WriteToJsonFile("result.json")
            .WriteToCsvFile("result.csv")
            .IgnoreUrls(blackList)
            .Build();

        runner = new ScraperRunner(config, spider, logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await runner.Run(10);
    }
}

