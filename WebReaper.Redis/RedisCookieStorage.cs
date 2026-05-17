using Microsoft.Extensions.Logging;
using WebReaper.Core.CookieStorage.Concrete;

namespace WebReaper.Redis;

/// <summary>
/// Redis cookie storage: the <see cref="CookieStore"/> payload shell (ADR
/// 0003) backed by a <see cref="RedisBlobStore"/>, shipped in the
/// WebReaper.Redis satellite (ADR 0009). The <c>logger</c>
/// parameter is vestigial — the payload shell needs none — kept so the
/// constructor matches what <c>WithRedisCookieStorage</c> passes.
/// </summary>
public class RedisCookieStorage : CookieStore
{
    public RedisCookieStorage(string connectionString, string redisKey, ILogger logger)
        : base(new RedisBlobStore(connectionString), redisKey)
    {
    }
}
