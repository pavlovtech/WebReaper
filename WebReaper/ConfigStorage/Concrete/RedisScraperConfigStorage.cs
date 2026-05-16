using Microsoft.Extensions.Logging;
using WebReaper.DataAccess;

namespace WebReaper.ConfigStorage.Concrete;

/// <summary>
/// Source-compatible constructor over the config <see cref="ScraperConfigStore"/>
/// payload shell backed by a <see cref="RedisBlobStore"/> (ADR 0003). The
/// <paramref name="logger"/> parameter is retained for binary/source
/// compatibility; serialization and missing-value policy now live in the
/// shell, so it is no longer used here.
/// </summary>
public class RedisScraperConfigStorage : ScraperConfigStore
{
    public RedisScraperConfigStorage(string connectionString, string redisKey, ILogger logger)
        : base(new RedisBlobStore(connectionString), redisKey)
    {
    }
}
