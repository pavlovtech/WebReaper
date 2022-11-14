using Newtonsoft.Json.Linq;
using WebReaper.Domain;
using WebReaper.LinkTracker.Abstract;
using WebReaper.Loaders.Abstract;
using WebReaper.Parser.Abstract;
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Models;

namespace WebReaper.Spider.Abstract;

public interface ISpider
{
    Task<List<Job>> CrawlAsync(Job job, CancellationToken cancellationToken = default);

    IStaticPageLoader StaticStaticPageLoader { get; init; }

    IBrowserPageLoader BrowserPageLoader { get; init; }

    ILinkParser LinkParser { get; init; }

    IContentParser ContentParser { get; init; }

    IVisitedLinkTracker LinkTracker { get; init; }

    public event Action<ParsedData>? ScrapedData;

    List<IScraperSink> Sinks { get; set; }

    List<string> UrlBlackList { get; set; }

    int PageCrawlLimit { get; set; }
}
