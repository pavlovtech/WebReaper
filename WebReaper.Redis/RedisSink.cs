using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using WebReaper.Infra.Abstract;
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Models;

namespace WebReaper.Redis;

public class RedisSink : IScraperSink, IAsyncInitializable
{
    private readonly IDatabase _db;
    private readonly ILogger _logger;
    private readonly string _redisKey;

    public RedisSink(string connectionString, string redisKey, bool dataCleanupOnStart, ILogger logger)
    {
        _db = RedisConnectionPool.GetDatabase(connectionString);
        DataCleanupOnStart = dataCleanupOnStart;
        _redisKey = redisKey;
        _logger = logger;

        _initialization = new Lazy<Task>(InitializeCoreAsync);
    }

    private readonly Lazy<Task> _initialization;
    public bool DataCleanupOnStart { get; set; }

    public async Task EmitAsync(ParsedData entity, CancellationToken cancellationToken = default)
    {
        // ADR-0031: the page URL is already folded into entity.Data by
        // ParsedData's construction — no per-sink merge.
        var db = _db;
        await db.SetAddAsync(_redisKey, entity.Data.ToString());
    }

    // ADR-0033: idempotent async warm-up, driven once before the crawl.
    public Task InitializeAsync() => _initialization.Value;

    private async Task InitializeCoreAsync()
    {
        if (!DataCleanupOnStart)
            return;

        var db = _db;

        await db.KeyDeleteAsync(_redisKey);
    }
}