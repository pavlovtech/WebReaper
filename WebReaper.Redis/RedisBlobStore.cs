using StackExchange.Redis;
using WebReaper.DataAccess;

namespace WebReaper.Redis;

/// <summary>
/// Redis-backed <see cref="IKeyedBlobStore"/>. The per-connection-string
/// multiplexer mechanism lives in <see cref="RedisConnectionPool"/> — the one
/// home shared by every Redis adapter (ADR 0005); this class only maps
/// <see cref="IKeyedBlobStore"/> onto string get/set.
/// </summary>
public class RedisBlobStore : IKeyedBlobStore
{
    private readonly ConnectionMultiplexer _redis;

    public RedisBlobStore(string connectionString) => _redis = RedisConnectionPool.Get(connectionString);

    public Task PutAsync(string key, string value)
        => _redis.GetDatabase().StringSetAsync(key, value);

    public async Task<string?> GetAsync(string key)
    {
        var value = await _redis.GetDatabase().StringGetAsync(key);
        return value.IsNull ? null : value.ToString();
    }
}
