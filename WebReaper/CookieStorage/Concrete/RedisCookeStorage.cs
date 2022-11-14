using System.Net;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using WebReaper.CookieStorage.Abstract;

namespace WebReaper.CookieStorage.Concrete;

public class RedisCookeStorage: ICookiesStorage
{
    private readonly ILogger _logger;
    private static ConnectionMultiplexer? redis;
    
    public RedisCookeStorage(string connectionString, ILogger logger)
    {
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

        await db.StringSetAsync($"cookies", JsonConvert.SerializeObject(cookieContainer));
    }

    public async Task<CookieContainer> GetAsync()
    {
        IDatabase db = redis!.GetDatabase();
        var json = db.StringGetAsync("cookies");

        var result = JsonConvert.DeserializeObject<CookieContainer>(json.ToString());
        return result;
    }
}