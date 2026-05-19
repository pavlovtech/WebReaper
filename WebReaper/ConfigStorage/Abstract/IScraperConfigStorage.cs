using WebReaper.Domain;

namespace WebReaper.ConfigStorage.Abstract;

/// <summary>
/// Where the immutable <see cref="ScraperConfig"/> lives so the Spider can
/// read it at crawl time. This is the seam that lets the in-process engine and
/// a stateless distributed worker share one crawl definition: the engine (or a
/// start-scraping endpoint) writes it once; every worker reads it back. The
/// durable adapters extend the <c>ScraperConfigStore</c> payload shell
/// (ADR-0003); satellites bind here (<c>RedisScraperConfigStorage</c>,
/// <c>MongoDbScraperConfigStorage</c>). In-memory by default.
/// </summary>
public interface IScraperConfigStorage
{
    /// <summary>
    /// Persist the crawl's <see cref="ScraperConfig"/>. Called once when the
    /// engine is built (<c>BuildAsync</c>), or by the start-scraping endpoint
    /// in the distributed pattern, before any Job is processed.
    /// </summary>
    Task CreateConfigAsync(ScraperConfig config);

    /// <summary>
    /// Read the persisted config. The Spider calls this at crawl time; in the
    /// distributed pattern it is how a stateless worker recovers the crawl
    /// definition it was never passed in-process.
    /// </summary>
    Task<ScraperConfig> GetConfigAsync();
}
