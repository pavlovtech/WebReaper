using Exoscan.Domain;
using Exoscan.LinkTracker.Abstract;
using Exoscan.Loaders.Abstract;
using Exoscan.Parser.Abstract;
using Exoscan.Sinks.Abstract;
using Exoscan.Sinks.Models;
using Newtonsoft.Json.Linq;

namespace Exoscan.Spider.Abstract;

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
