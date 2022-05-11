using System.Collections.Concurrent;
using WebReaper.Domain;
using WebReaper.Domain.Parsing;
using WebReaper.Parsing;
using WebReaper.Queue;
using WebReaper.Queue.InMemory;
using WebReaper.Scraper;

namespace ScraperWorkerService;

public class ScrapingWorker : BackgroundService
{
    private ScraperRunner runner;

    public ScrapingWorker(ILogger<ScrapingWorker> logger)
    {
        var blackList = new string[] {
            "https://rutracker.org/forum/viewforum.php?f=396",
            "https://rutracker.org/forum/viewforum.php?f=2322",
            "https://rutracker.org/forum/viewforum.php?f=1993",
            "https://rutracker.org/forum/viewforum.php?f=2167",
            "https://rutracker.org/forum/viewforum.php?f=2321"
        };

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
            .WriteToJsonFile("result.json")
            .WriteToCsvFile("result.csv")
            .IgnoreUrls(blackList)
            .WithLogger(logger)
            .Build();

        BlockingCollection<Job> jobs = new(new ProducerConsumerPriorityQueue());
        var jobQueueReader = new JobQueueReader(jobs);
        var jobQueueWriter = new JobQueueWriter(jobs);

        runner = new ScraperRunner(config, jobQueueReader, jobQueueWriter,  spider, logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await runner.Run(10);
    }
}

