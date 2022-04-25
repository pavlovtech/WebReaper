namespace WebReaper.Abastracts.Spider;

public interface ISpider
{
    Task Crawl();
    ISpider IgnoreUrls(params string[] urlBlackList);
    ISpider Limit(int limit);
}
