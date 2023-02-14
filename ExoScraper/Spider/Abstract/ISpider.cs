using ExoScraper.Domain;
using Newtonsoft.Json.Linq;

namespace ExoScraper.Spider.Abstract;

public interface ISpider
{
    Task<List<Job>> CrawlAsync(Job job, CancellationToken cancellationToken = default);
}
