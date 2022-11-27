using Exoscan.DataAccess;
using Exoscan.Sinks.Abstract;
using Exoscan.Sinks.Models;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Exoscan.Sinks.Concrete;

public class RedisSink : RedisBase, IScraperSink
{
    private readonly string _redisKey;
    private readonly ILogger _logger;

    public RedisSink(string connectionString, string redisKey, ILogger logger): base(connectionString)
    {
        _redisKey = redisKey;
        _logger = logger;
    }

    public async Task EmitAsync(ParsedData entity, CancellationToken cancellationToken = default)
    {
        entity.Data["url"] = entity.Url;
        
        var db = Redis.GetDatabase();
        await db.SetAddAsync(_redisKey, entity.Data.ToString());
    }
}