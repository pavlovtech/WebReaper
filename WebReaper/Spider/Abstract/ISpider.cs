namespace WebReaper.Spider.Abastract;

public interface ISpider
{
    Task Crawl();
    ISpider IgnoreUrls(params string[] urlBlackList);
}
