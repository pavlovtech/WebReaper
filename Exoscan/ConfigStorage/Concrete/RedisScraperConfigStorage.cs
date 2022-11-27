using Exoscan.ConfigStorage.Abstract;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace Exoscan.ConfigStorage.Concrete;

public class RedisScraperConfigStorage: IScraperConfigStorage
{
    private readonly string _redisKey;
    private readonly ILogger _logger;
    private static ConnectionMultiplexer? redis;

    public RedisScraperConfigStorage(string connectionString, string? redisKey, ILogger? logger)
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
    
    public async Task CreateConfigAsync(ScraperConfig config)
    {
        var db = redis!.GetDatabase();

        await db.StringSetAsync(_redisKey, SerializeToJson(config));
    }

    public async Task<ScraperConfig> GetConfigAsync()
    {
        IDatabase db = redis!.GetDatabase();
        var json = await db.StringGetAsync(_redisKey);

        var result = JsonConvert.DeserializeObject<ScraperConfig>(json.ToString());
        return result;
    }
    
    private string SerializeToJson(ScraperConfig config)
    {
        var json = JsonConvert.SerializeObject(config, Formatting.Indented, new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto,
            NullValueHandling = NullValueHandling.Ignore,
            TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple
        });

        return json;
    }
}