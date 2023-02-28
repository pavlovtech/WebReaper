using Newtonsoft.Json.Linq;
using WebReaper.Domain;

namespace WebReaper.Spider.Abstract;

public interface ISpider
{
    Task<List<Job>> CrawlAsync(Job job, CancellationToken cancellationToken = default);
}
