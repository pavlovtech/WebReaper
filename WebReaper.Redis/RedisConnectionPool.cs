using System.Collections.Concurrent;
using StackExchange.Redis;

namespace WebReaper.Redis;

/// <summary>
/// The one home for WebReaper's StackExchange.Redis connection mechanism:
/// exactly one <see cref="ConnectionMultiplexer"/> per distinct connection
/// string, created lazily and thread-safely (ADR 0005). Replaces
/// <c>RedisBase</c>, whose process-<c>static</c>, first-connection-wins
/// multiplexer silently ignored every connection string after the first — a
/// latent bug for the distributed mode the README sells. Every Redis-backed
/// adapter (<see cref="RedisBlobStore"/> and the Redis scheduler, sink and
/// visited-link tracker) resolves through here, so the connect tuning and the
/// per-connection-string identity rule live in exactly one place.
/// </summary>
public static class RedisConnectionPool
{
    private static readonly ConcurrentDictionary<string, Lazy<ConnectionMultiplexer>> Multiplexers = new();

    /// <summary>
    /// The shared multiplexer for <paramref name="connectionString"/>. Same
    /// string ⇒ same instance; different string ⇒ different instance. With
    /// <c>AbortOnConnectFail = false</c> this does not throw when no server is
    /// reachable — it returns a multiplexer that reconnects in the background.
    /// </summary>
    public static ConnectionMultiplexer Get(string connectionString) =>
        Multiplexers.GetOrAdd(connectionString, cs =>
            new Lazy<ConnectionMultiplexer>(() => ConnectionMultiplexer.Connect(cs, config =>
            {
                config.AbortOnConnectFail = false;
                config.AllowAdmin = true;
                config.AsyncTimeout = 180000;
                config.SyncTimeout = 180000;
                config.ReconnectRetryPolicy = new ExponentialRetry(10000);
            }))).Value;

    /// <summary>The default database on the shared multiplexer for <paramref name="connectionString"/>.</summary>
    public static IDatabase GetDatabase(string connectionString) => Get(connectionString).GetDatabase();
}
