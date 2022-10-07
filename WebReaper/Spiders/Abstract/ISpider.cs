using Newtonsoft.Json.Linq;
using WebReaper.Core.Domain;
using WebReaper.Core.LinkTracker.Abstract;
using WebReaper.Core.Loaders.Abstract;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Core.Sinks.Abstract;

namespace WebReaper.Core.Spiders.Abstract;

public interface ISpider
{
    Task<IEnumerable<Job>> CrawlAsync(Job job);

    IStaticPageLoader StaticStaticPageLoader { get; init; }

    IDynamicPageLoader DynamicPageLoader { get; init; }

    ILinkParser LinkParser { get; init; }

    IContentParser ContentParser { get; init; }

    ICrawledLinkTracker LinkTracker { get; init; }

    public event Action<JObject> ScrapedData;

    List<IScraperSink> Sinks { get; init; }

    List<string> UrlBlackList { get; set; }

    int PageCrawlLimit { get; set; }
}
