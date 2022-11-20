using Exoscan.Domain;
using Newtonsoft.Json.Linq;

namespace Exoscan.Spider.Abstract;

public interface ISpider
{
    Task<List<Job>> CrawlAsync(Job job, CancellationToken cancellationToken = default);
}
