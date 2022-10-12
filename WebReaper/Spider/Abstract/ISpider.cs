using Newtonsoft.Json.Linq;
using WebReaper.Domain;
using WebReaper.LinkTracker.Abstract;
using WebReaper.Loaders.Abstract;
using WebReaper.Parser.Abstract;
using WebReaper.Sinks.Abstract;

namespace WebReaper.Spider.Abstract;

public interface ISpider
{
    Task<IEnumerable<Job>> CrawlAsync(Job job);

    IStaticPageLoader StaticStaticPageLoader { get; init; }

    IDynamicPageLoader DynamicPageLoader { get; init; }

    ILinkParser LinkParser { get; init; }

    IContentParser ContentParser { get; init; }

    public event Action<JObject> ScrapedData;

    List<IScraperSink> Sinks { get; init; }

    List<string> UrlBlackList { get; set; }

    int Limit { get; set; }
}
