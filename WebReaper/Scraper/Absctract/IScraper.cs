using System.Net;
using WebReaper.Domain;

namespace WebReaper.Scraper.Abstract;

public interface IScraper
{
    IScraper WithStartUrl(string startUrl);
    IScraper FollowLinks(string linkSelector, SelectorType selectorType = SelectorType.Css);
    IScraper Limit(int limit);
    IScraper IgnoreUrls(params string[] urls);
    IScraper Paginate(string paginationSelector);
    Task Run();
    IScraper To(string filePath);
    IScraper WithProxy(WebProxy proxy);
    IScraper WithProxy(WebProxy[] proxies);
    IScraper WithPuppeter(WebProxy[] proxies);
    IScraper WithScheme(SchemaElement[] schema);
    IScraper WithParallelismDegree(int parallelismDegree);
    IScraper Authorize(Func<CookieContainer> authorize);
}
