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
