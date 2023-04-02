using WebReaper.Domain;

namespace WebReaper.Core.Spider.Abstract;

public interface ISpider
{
    Task<List<Job>> CrawlAsync(Job job, CancellationToken cancellationToken = default);
}