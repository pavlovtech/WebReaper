using System.Collections.Concurrent;
using StackExchange.Redis;

namespace WebReaper.DataAccess;

/// <summary>
/// Redis-backed <see cref="IKeyedBlobStore"/>. Owns <em>one multiplexer per
/// distinct connection string</em> (the pattern StackExchange.Redis
/// recommends), replacing <c>RedisBase</c>'s process-static, first-connection-
/// wins multiplexer that silently ignored every connection string after the
/// first — a latent bug for the distributed mode the README sells (ADR 0003).
/// </summary>
public class RedisBlobStore : IKeyedBlobStore
{
    private static readonly ConcurrentDictionary<string, Lazy<ConnectionMultiplexer>> Multiplexers = new();

    private readonly ConnectionMultiplexer _redis;

    public RedisBlobStore(string connectionString)
    {
        _redis = Multiplexers.GetOrAdd(connectionString, cs =>
            new Lazy<ConnectionMultiplexer>(() => ConnectionMultiplexer.Connect(cs, config =>
            {
                config.AbortOnConnectFail = false;
                config.AllowAdmin = true;
                config.AsyncTimeout = 180000;
                config.SyncTimeout = 180000;
                config.ReconnectRetryPolicy = new ExponentialRetry(10000);
            }))).Value;
    }

    public Task PutAsync(string key, string value)
        => _redis.GetDatabase().StringSetAsync(key, value);

    public async Task<string?> GetAsync(string key)
    {
        var value = await _redis.GetDatabase().StringGetAsync(key);
        return value.IsNull ? null : value.ToString();
    }
}
