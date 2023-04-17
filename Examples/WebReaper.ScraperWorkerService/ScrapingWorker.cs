using AngleSharp;
using WebReaper.Core;
using WebReaper.Domain;
using Newtonsoft.Json.Linq;
using WebReaper.Builders;

namespace WebReaper.ScraperWorkerService;

public class ScrapingWorker : BackgroundService
{
    private readonly ILogger<ScrapingWorker> _logger;

    public ScrapingWorker(ILogger<ScrapingWorker> logger)
    {
        _logger = logger;
    }
   
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var blackList = new[] {
            "https://rutracker.org/forum/viewforum.php?f=396",
            "https://rutracker.org/forum/viewforum.php?f=2322",
            "https://rutracker.org/forum/viewforum.php?f=1993",
            "https://rutracker.org/forum/viewforum.php?f=2167",
            "https://rutracker.org/forum/viewforum.php?f=2321"
        };

        var engine = await new ScraperEngineBuilder()
            .WithLogger(_logger)
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
            .IgnoreUrls(blackList)
            .PostProcess(ParseTorrentStats)
            .WithRedisScheduler("localhost:6379", "jobs", true)
            .TrackVisitedLinksInRedis("localhost:6379", "rutracker-visited-links", true)
            .WriteToRedis("localhost:6379", "rutracker-audiobooks", true)
            .WithRedisConfigStorage("localhost:6379", "rutracker-scraper-config")
            .WithParallelismDegree(20)
            .BuildAsync();

        await engine.RunAsync(stoppingToken);
    }

    private static async Task ParseTorrentStats(Metadata meta, JObject result)
    {
        var config = Configuration.Default.WithDefaultLoader();
        var context = BrowsingContext.New(config);

        var document = await context.OpenAsync(meta.BackLinks.Last());

        var torrentRow = document
            .QuerySelectorAll("tr.hl-tr")
            .FirstOrDefault(e => e.QuerySelector($".torTopic>a[href*='{new Uri(meta.Url).Query}']") != null);

        bool seedsParsed = int.TryParse(torrentRow?.QuerySelector(".seedmed")?.TextContent, out var seeds);
        bool leechesParsed = int.TryParse(torrentRow?.QuerySelector(".leechmed")?.TextContent, out var leeches);
        bool replaysCountParsed =
            int.TryParse(torrentRow?.QuerySelector("span[title='Ответов']")?.TextContent, out var replaysCount);
        bool downloadsCountParsed =
            int.TryParse(
                torrentRow?.QuerySelector("td.vf-col-replies")?.QuerySelector("p:nth-child(2)")?.TextContent
                    .Replace(",", ""), out int downloadsCount);

        result["seeds"] = seedsParsed ? seeds : null;
        result["leeches"] = leechesParsed ? leeches : null;
        result["downloads"] = downloadsCountParsed ? downloadsCount : null;
        result["replays"] = replaysCountParsed ? replaysCount : null;
    }
}

