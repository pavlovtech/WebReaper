using System.Net;
using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;
using WebReaper.Domain;
using WebReaper.Scraper.Abstract;
using WebReaper.Scraper.Concrete;

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
                new("coverImageUrl", ".postImg", ContentType.Image),
                new("name", "div.postbody>span"),
                new("category", "td:nth-child(2)>span>a:nth-child(2)"),
                new("subcategory", "td:nth-child(2)>span>a:nth-child(3)"),
                new("torrentSize", "td.genmed>span"),
                new("torrentLink", "a[href*='download.php?']", ContentType.Url)
            })
            .WithParallelismDegree(10)
            .WriteTo(new FileSink("result.json"))
            .WriteTo(new ConsoleSink());
    }

    protected CookieContainer Auth() {
        CookieContainer cookies = new CookieContainer();

        var web = new HtmlWeb();
        var doc = web.Load("https://nnmclub.to/forum/login.php");
        var code = doc.DocumentNode.QuerySelector("input[type=hidden][name=code]").GetAttributeValue("value", "");

        HttpClientHandler handler = new HttpClientHandler();
        handler.CookieContainer = cookies;

        var httpClient = new HttpClient(handler);

        var formContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("username", "gif0"), 
            new KeyValuePair<string, string>("password", "111111"),
            new KeyValuePair<string, string>("autologin", "on"),
            new KeyValuePair<string, string>("redirect", ""),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("login", "Вход") 
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

