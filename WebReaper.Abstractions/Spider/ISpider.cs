using WebReaper.Absctracts.Sinks;
using WebReaper.Abstractions.JobQueue;
using WebReaper.Abstractions.Loaders.PageLoader;
using WebReaper.Abstractions.Parsers;
using WebReaper.LinkTracker.Abstract;

namespace WebReaper.Abastracts.Spider;

public interface ISpider
{
    Task CrawlAsync();

    IPageLoader StaticPageLoader { get; init; }
    
    IPageLoader SpaPageLoader { get; init; }

    ILinkParser LinkParser { get; init; }

    IContentParser ContentParser { get; init; }

    ILinkTracker LinkTracker { get; init; }

    IJobQueueReader JobQueueReader { get; init; }

    IJobQueueWriter JobQueueWriter { get; init; }

    List<IScraperSink> Sinks { get; init; }

    List<string> UrlBlackList { get; set; }

    int PageCrawlLimit { get; set; }
}
