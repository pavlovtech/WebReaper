using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Models;

namespace WebReaper.Redis;

public class RedisSink : IScraperSink
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

        Initialization = InitializeAsync();
    }

    private Task Initialization { get; }
    public bool DataCleanupOnStart { get; set; }

    public async Task EmitAsync(ParsedData entity, CancellationToken cancellationToken = default)
    {
        await Initialization;

        // ADR-0031: the page URL is already folded into entity.Data by
        // ParsedData's construction — no per-sink merge.
        var db = _db;
        await db.SetAddAsync(_redisKey, entity.Data.ToString());
    }

    private async Task InitializeAsync()
    {
        if (!DataCleanupOnStart)
            return;

        var db = _db;

        await db.KeyDeleteAsync(_redisKey);
    }
}