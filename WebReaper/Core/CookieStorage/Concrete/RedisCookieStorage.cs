using Microsoft.Extensions.Logging;
using WebReaper.DataAccess;

namespace WebReaper.Core.CookieStorage.Concrete;

/// <summary>
/// Source-compatible constructor over the <see cref="CookieStore"/> payload
/// shell backed by a <see cref="RedisBlobStore"/> (ADR 0003). The
/// <paramref name="logger"/> parameter is retained for binary/source
/// compatibility; it is no longer used here.
/// </summary>
public class RedisCookieStorage : CookieStore
{
    public RedisCookieStorage(string connectionString, string redisKey, ILogger logger)
        : base(new RedisBlobStore(connectionString), redisKey)
    {
    }
}
