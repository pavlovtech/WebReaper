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

        var redisConnectionString = "redis-14134.c135.eu-central-1-1.ec2.cloud.redislabs.com:14134,allowAdmin=true,password=fFyL97L9hj3NPTsIGyPy99YgxnmoHzH4";
        var azureSBConnectionString = "Endpoint=sb://webreaper.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=mIHXjIKh6I89CHyMM2SDMr7YxvVTDFQvL+/FKlbK43g=";
        var queue = "jobqueue";

        scraper = new Scraper()
            .WithLogger(logger)
            .WithStartUrl("https://rutracker.org/forum/index.php?c=33")
            .IgnoreUrls(blackList)
            .FollowLinks("#cf-33 .forumlink>a")
            .FollowLinks(".forumlink>a")
            .FollowLinks("a.torTopic", ".pg")
            .WithScheme(new Schema {
                new("name", "#topic-title"),
                new("nested") {
                    new("category", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(3)"),
                    new("subcategory", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(5)")
                },
                new("category", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(3)"),
                new("subcategory", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(5)"),
                new("torrentSize", "div.attach_link.guest>ul>li:nth-child(2)"),
                new Url("torrentLink", ".magnet-link"),
                new Image("coverImageUrl", ".postImg")
            })
            .WithParallelismDegree(10)
            .WithLinkTracker(new RedisCrawledLinkTracker(redisConnectionString))
            .WithJobQueueReader(new AzureJobQueueReader(azureSBConnectionString, queue))
            .WithJobQueueWriter(new AzureJobQueueWriter(azureSBConnectionString, queue))
            .WriteToJsonFile("result.json")
            .WriteToCsvFile("result.csv")
            .Build();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await scraper.Run();
    }
}

