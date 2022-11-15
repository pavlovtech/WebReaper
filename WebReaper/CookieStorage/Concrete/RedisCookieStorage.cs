using System.Net;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using WebReaper.CookieStorage.Abstract;

namespace WebReaper.CookieStorage.Concrete;

public class RedisCookieStorage: ICookiesStorage
{
    private readonly string _redisKey;
    private readonly ILogger _logger;
    private static ConnectionMultiplexer? redis;
    
    public RedisCookieStorage(string connectionString, string redisKey, ILogger logger)
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
    
    public async Task AddAsync(CookieContainer cookieContainer, TimeSpan timeToLive)
    {
        IDatabase db = redis!.GetDatabase();

        await db.StringSetAsync(_redisKey, JsonConvert.SerializeObject(cookieContainer));
    }

    public async Task<CookieContainer> GetAsync()
    {
        IDatabase db = redis!.GetDatabase();
        var json = db.StringGetAsync(_redisKey);

        var result = JsonConvert.DeserializeObject<CookieContainer>(json.ToString());
        return result;
    }
}