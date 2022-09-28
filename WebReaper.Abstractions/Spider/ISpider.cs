using WebReaper.Abstractions.JobQueue;
using WebReaper.Abstractions.LinkTracker;
using WebReaper.Abstractions.Loaders;
using WebReaper.Abstractions.Parsers;
using WebReaper.Abstractions.Sinks;
using WebReaper.Domain;

namespace WebReaper.Abstractions.Spider;

public interface ISpider
{
    Task<IEnumerable<Job>> CrawlAsync(Job job);

    IPageLoader StaticPageLoader { get; init; }

    IPageLoader SpaPageLoader { get; init; }

    ILinkParser LinkParser { get; init; }

    IContentParser ContentParser { get; init; }

    ICrawledLinkTracker LinkTracker { get; init; }

    List<IScraperSink> Sinks { get; init; }

    List<string> UrlBlackList { get; set; }

    int PageCrawlLimit { get; set; }
}
