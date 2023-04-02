using Microsoft.Extensions.Logging;
using WebReaper.DataAccess;
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Models;

namespace WebReaper.Sinks.Concrete;

public class RedisSink : RedisBase, IScraperSink
{
    private readonly ILogger _logger;
    private readonly string _redisKey;

    public RedisSink(string connectionString, string redisKey, bool dataCleanupOnStart, ILogger logger) : base(
        connectionString)
    {
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

        entity.Data["url"] = entity.Url;

        var db = Redis.GetDatabase();
        await db.SetAddAsync(_redisKey, entity.Data.ToString());
    }

    private async Task InitializeAsync()
    {
        if (!DataCleanupOnStart)
            return;

        var db = Redis.GetDatabase();

        await db.KeyDeleteAsync(_redisKey);
    }
}