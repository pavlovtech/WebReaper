using System.Net;
using WebReaper.Domain;
using WebReaper.Scraper.Concrete;
using WebReaper.Sinks.Absctract;

namespace WebReaper.Scraper.Abstract;

public interface IScraper
{
    IScraper WithStartUrl(string startUrl);
    IScraper FollowLinks(string linkSelector, SelectorType selectorType = SelectorType.Css);
    ScraperSinkConfig WriteTo { get; }
    IScraper AddSink(IScraperSink sink);
    IScraper Limit(int limit);
    IScraper IgnoreUrls(params string[] urls);
    IScraper Paginate(string paginationSelector);
    IScraper Build();
    Task Run();
    IScraper WithProxy(WebProxy proxy);
    IScraper WithProxy(WebProxy[] proxies);
    IScraper WithPuppeter(WebProxy[] proxies);
    IScraper WithScheme(SchemaElement[] schema);
    IScraper WithParallelismDegree(int parallelismDegree);
    IScraper Authorize(Func<CookieContainer> authorize);
}
