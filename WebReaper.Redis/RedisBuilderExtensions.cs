using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Builders;

namespace WebReaper.Redis;

/// <summary>
/// ADR-0009: the Redis builder sugar lives here, in the WebReaper.Redis
/// satellite, over <see cref="ScraperEngineBuilder"/>'s public registration
/// seam (<c>AddSink</c> / <c>WithScheduler</c> / <c>WithLinkTracker</c> /
/// <c>WithConfigStorage</c> / <c>WithCookieStorage</c>) — not in core. Core no
/// longer references StackExchange.Redis. A satellite extension cannot reach
/// the builder's private logger, so the logger is an explicit optional
/// argument (defaulting to <see cref="NullLogger"/>) rather than the builder's.
/// </summary>
public static class RedisBuilderExtensions
{
    /// <summary>
    /// Registers a Redis sink: each parsed item is appended under the given
    /// Redis key, over <see cref="ScraperEngineBuilder"/>'s public
    /// <c>AddSink</c> seam.
    /// </summary>
    /// <param name="builder">The scraper engine builder to add the sink to.</param>
    /// <param name="connectionString">StackExchange.Redis connection string.</param>
    /// <param name="redisKey">Redis key the results are written under.</param>
    /// <param name="dataCleanupOnStart">When <see langword="true"/>, existing data is cleared when the scrape starts.</param>
    /// <param name="logger">Optional logger; defaults to <see cref="NullLogger"/> (a satellite extension cannot reach the builder's private logger).</param>
    /// <returns>The same <see cref="ScraperEngineBuilder"/>, for chaining.</returns>
    public static ScraperEngineBuilder WriteToRedis(
        this ScraperEngineBuilder builder,
        string connectionString,
        string redisKey,
        bool dataCleanupOnStart = false,
        ILogger? logger = null)
    {
        return builder.AddSink(new RedisSink(
            connectionString,
            redisKey,
            dataCleanupOnStart,
            logger ?? NullLogger.Instance));
    }

    /// <summary>
    /// Uses a Redis-backed job queue as the scheduler, over
    /// <see cref="ScraperEngineBuilder"/>'s public <c>WithScheduler</c> seam —
    /// so multiple workers can share crawl state (distributed mode).
    /// </summary>
    /// <param name="builder">The scraper engine builder.</param>
    /// <param name="connectionString">StackExchange.Redis connection string.</param>
    /// <param name="queueName">Redis key used as the shared job queue.</param>
    /// <param name="dataCleanupOnStart">When <see langword="true"/>, the queue is cleared when the scrape starts.</param>
    /// <param name="logger">Optional logger; defaults to <see cref="NullLogger"/>.</param>
    /// <returns>The same <see cref="ScraperEngineBuilder"/>, for chaining.</returns>
    public static ScraperEngineBuilder WithRedisScheduler(
        this ScraperEngineBuilder builder,
        string connectionString,
        string queueName,
        bool dataCleanupOnStart = false,
        ILogger? logger = null)
    {
        return builder.WithScheduler(new RedisScheduler(
            connectionString,
            queueName,
            logger ?? NullLogger.Instance,
            dataCleanupOnStart));
    }

    /// <summary>
    /// Tracks visited links in Redis, over <see cref="ScraperEngineBuilder"/>'s
    /// public <c>WithLinkTracker</c> seam — so workers in distributed mode
    /// don't re-crawl each other's pages.
    /// </summary>
    /// <param name="builder">The scraper engine builder.</param>
    /// <param name="connectionString">StackExchange.Redis connection string.</param>
    /// <param name="redisKey">Redis key holding the visited-link set.</param>
    /// <param name="dataCleanupOnStart">When <see langword="true"/>, the visited-link set is cleared when the scrape starts.</param>
    /// <returns>The same <see cref="ScraperEngineBuilder"/>, for chaining.</returns>
    public static ScraperEngineBuilder TrackVisitedLinksInRedis(
        this ScraperEngineBuilder builder,
        string connectionString,
        string redisKey,
        bool dataCleanupOnStart = false)
    {
        return builder.WithLinkTracker(new RedisVisitedLinkTracker(
            connectionString,
            redisKey,
            dataCleanupOnStart));
    }

    /// <summary>
    /// Stores the immutable scraper config in Redis, over
    /// <see cref="ScraperEngineBuilder"/>'s public <c>WithConfigStorage</c>
    /// seam — so multiple workers can share crawl configuration.
    /// </summary>
    /// <param name="builder">The scraper engine builder.</param>
    /// <param name="connectionString">StackExchange.Redis connection string.</param>
    /// <param name="redisKey">Redis key the config is stored under.</param>
    /// <param name="logger">Optional logger; defaults to <see cref="NullLogger"/>.</param>
    /// <returns>The same <see cref="ScraperEngineBuilder"/>, for chaining.</returns>
    public static ScraperEngineBuilder WithRedisConfigStorage(
        this ScraperEngineBuilder builder,
        string connectionString,
        string redisKey,
        ILogger? logger = null)
    {
        return builder.WithConfigStorage(new RedisScraperConfigStorage(
            connectionString,
            redisKey,
            logger ?? NullLogger.Instance));
    }

    /// <summary>
    /// Stores session cookies in Redis, over
    /// <see cref="ScraperEngineBuilder"/>'s public <c>WithCookieStorage</c>
    /// seam — so multiple workers can share an authenticated session.
    /// </summary>
    /// <param name="builder">The scraper engine builder.</param>
    /// <param name="connectionString">StackExchange.Redis connection string.</param>
    /// <param name="redisKey">Redis key the cookies are stored under.</param>
    /// <param name="logger">Optional logger; defaults to <see cref="NullLogger"/>.</param>
    /// <returns>The same <see cref="ScraperEngineBuilder"/>, for chaining.</returns>
    public static ScraperEngineBuilder WithRedisCookieStorage(
        this ScraperEngineBuilder builder,
        string connectionString,
        string redisKey,
        ILogger? logger = null)
    {
        return builder.WithCookieStorage(new RedisCookieStorage(
            connectionString,
            redisKey,
            logger ?? NullLogger.Instance));
    }
}
