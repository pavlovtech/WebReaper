using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Models;

namespace WebReaper.Sinks.Concrete;

public class RedisSink : IScraperSink
{
    private readonly string _redisKey;
    private readonly ILogger _logger;
    private static ConnectionMultiplexer redis;
    
    public RedisSink(string connectionString, string redisKey, ILogger logger)
    {
        _redisKey = redisKey;
        _logger = logger;
        redis = ConnectionMultiplexer.Connect(connectionString, config =>
        {
            config.AbortOnConnectFail = false;

            config.AsyncTimeout = 180000;
            config.SyncTimeout = 180000;

            config.ReconnectRetryPolicy = new ExponentialRetry(10000);
        });
    }

    public async Task EmitAsync(ParsedData entity, CancellationToken cancellationToken = default)
    {
        entity.Data["url"] = entity.Url;
        
        var db = redis.GetDatabase();
        await db.SetAddAsync(_redisKey, entity.Data.ToString());
    }
}