using WebReaper.Domain.Parsing;
using WebReaper.LinkTracker;
using WebReaper.Parsing;
using WebReaper.Queue.AzureServiceBus;
using WebReaper.Scraper;

namespace DistributedScraperWorkerService;

public class ScrapingWorker : BackgroundService
{
    private readonly Scraper scraper;

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

        scraper = new Scraper()
            .WithLogger(logger)
            .WithStartUrl("https://rutracker.org/forum/index.php?c=33")
            .FollowLinks("#cf-33 .forumlink>a")
            .FollowLinks(".forumlink>a")
            .FollowLinks("a.torTopic", ".pg")
            .IgnoreUrls(blackList)
            .Parse(new Schema {
                new("name", "#topic-title"),
                new("category", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(3)"),
                new("subcategory", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(5)"),
                new("torrentSize", "div.attach_link.guest>ul>li:nth-child(2)"),
                new Url("torrentLink", ".magnet-link"),
                new Image("coverImageUrl", ".postImg")
            })
            .WithLinkTracker(new RedisCrawledLinkTracker(redisConnectionString))
            .WriteToCosmosDb(
                "https://webreaper.documents.azure.com:443/",
                "XkMSndeYQ1285XrVRNG7MYVg3YUw32aOPPpYyS8YDIcKa8SxMK5cqwsg069jlFW2oOdxedg92qQieZd0IO4Qtw==",
                "WebReaper",
                "Rutraker")
            .WithJobQueueReader(new AzureJobQueueReader(azureSBConnectionString, queue))
            .WithJobQueueWriter(new AzureJobQueueWriter(azureSBConnectionString, queue));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await scraper.Run(10);
    }
}

