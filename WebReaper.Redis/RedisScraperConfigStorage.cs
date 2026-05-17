using Microsoft.Extensions.Logging;
using WebReaper.ConfigStorage.Concrete;

namespace WebReaper.Redis;

/// <summary>
/// Redis scraper-config storage: the config <see cref="ScraperConfigStore"/>
/// payload shell (ADR 0003) backed by a <see cref="RedisBlobStore"/>, shipped
/// in the WebReaper.Redis satellite (ADR 0009). The <paramref name="logger"/>
/// parameter is vestigial — serialization and missing-value policy live in
/// the shell — kept so the constructor matches what
/// <c>WithRedisConfigStorage</c> passes.
/// </summary>
public class RedisScraperConfigStorage : ScraperConfigStore
{
    public RedisScraperConfigStorage(string connectionString, string redisKey, ILogger logger)
        : base(new RedisBlobStore(connectionString), redisKey)
    {
    }
}
