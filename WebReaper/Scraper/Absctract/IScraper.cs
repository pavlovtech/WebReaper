using System.Net;
using WebReaper.Domain;

namespace WebReaper.Scraper.Abstract;

public interface IScraper
{
    IScraper WithStartUrl(string startUrl);
    IScraper FollowLinks(string linkSelector);
    IScraper Limit(int limit);
    IScraper Paginate(string paginationSelector);
    Task Run();
    IScraper To(string filePath);
    IScraper WithProxy(WebProxy proxy);
    IScraper WithProxy(WebProxy[] proxies);
    IScraper WithPuppeter(WebProxy[] proxies);
    IScraper WithScheme(WebEl[] schema);
    IScraper WithSpiders(int spiderCount);
}
