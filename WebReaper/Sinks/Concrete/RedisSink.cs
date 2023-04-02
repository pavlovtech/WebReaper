using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using WebReaper.DataAccess;
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Models;

namespace WebReaper.Sinks.Concrete;

public class RedisSink : RedisBase, IScraperSink
{
    private readonly string _redisKey;
    private readonly ILogger _logger;
    public bool DataCleanupOnStart { get; set; }

    private Task Initialization { get; set; }

    public RedisSink(string connectionString, string redisKey, bool dataCleanupOnStart, ILogger logger): base(connectionString)
    {
        DataCleanupOnStart = dataCleanupOnStart;
        _redisKey = redisKey;
        _logger = logger;
        
        Initialization = InitializeAsync();
    }
    
    private async Task InitializeAsync()
    {
        if (!DataCleanupOnStart)
            return;
        
        var db = Redis.GetDatabase();

        await db.KeyDeleteAsync(_redisKey);
    }

    public async Task EmitAsync(ParsedData entity, CancellationToken cancellationToken = default)
    {
        await Initialization;
        
        entity.Data["url"] = entity.Url;
        
        var db = Redis.GetDatabase();
        await db.SetAddAsync(_redisKey, entity.Data.ToString());
    }
}