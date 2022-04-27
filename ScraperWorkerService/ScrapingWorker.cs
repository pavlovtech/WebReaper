using System.Net;
using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;
using WebReaper.Abstractions.Scraper;
using WebReaper.Domain.Schema;
using WebReaper.Schema;
using WebReaper.Scraper;

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
            .IgnoreUrls(blackList)
            .FollowLinks("#cf-33 .forumlink>a")
            .FollowLinks(".forumlink>a")
            .FollowLinks("a.torTopic")
            .Paginate(".pg")
            .WithScheme(new SchemaElement[] {
                //new ImageSchemaElement("coverImageUrl", ".postImg"),
                new("name", "#topic-title"),
                new("category", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(3)"),
                new("subcategory", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(5)"),
                new("torrentSize", "div.attach_link.guest>ul>li:nth-child(2)"),
                new Url("torrentLink", ".magnet-link")
            })
            .WithParallelismDegree(1)
            .WriteToJsonFile("result.json")
            .WriteToCsvFile("result.csv")
            .Build();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await scraper.Run();
    }
}

