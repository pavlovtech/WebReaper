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

        scraper = new Scraper(logger)
            .WithStartUrl("https://nnmclub.to/forum/viewforum.php?f=438")
            .Authorize(() => Auth())
            .FollowLinks("h2>a.forumlink")
            .FollowLinks("a.topictitle")
            .Paginate("td>span.nav>a[href*='start=']")
            .WithScheme(new SchemaElement[] {
                new TextSchemaElement("name","div.postbody>span"),
                new TextSchemaElement("category", "td:nth-child(2)>span>a:nth-child(2)"),
                new TextSchemaElement("subcategory", "td:nth-child(2)>span>a:nth-child(3)"),
                new TextSchemaElement("torrentSize", "td.genmed>span"),
                new UrlSchemaElement("torrentLink", "a[href*='download.php?']"),
                new ImageSchemaElement("coverImageUrl", ".postImg")
            })
            .WithParallelismDegree(10)
            .WriteToJsonFile("result.json")
            .WriteToCsvFile("result.csv")
            .Build();
    }

    protected CookieContainer Auth() {
        CookieContainer cookies = new();

        var web = new HtmlWeb();
        var doc = web.Load("https://nnmclub.to/forum/login.php");
        var code = doc.DocumentNode.QuerySelector("input[type=hidden][name=code]").GetAttributeValue("value", "");

        HttpClientHandler handler = new() {
            CookieContainer = cookies
        };

        var httpClient = new HttpClient(handler);

        var formContent = new FormUrlEncodedContent(new KeyValuePair<string, string>[]
        {
            new("username", "gif0"), 
            new("password", "111111"),
            new("autologin", "on"),
            new("redirect", ""),
            new("code", code),
            new("login", "Вход") 
        });

        var resp = httpClient
            .PostAsync("https://nnmclub.to/forum/login.php", formContent)
            .GetAwaiter()
            .GetResult();

        return cookies;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await scraper.Run();
    }
}

