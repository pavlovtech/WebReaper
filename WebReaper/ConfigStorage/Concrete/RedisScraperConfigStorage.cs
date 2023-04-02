using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using WebReaper.ConfigStorage.Abstract;
using WebReaper.DataAccess;
using WebReaper.Domain;

namespace WebReaper.ConfigStorage.Concrete;

public class RedisScraperConfigStorage : RedisBase, IScraperConfigStorage
{
    private readonly ILogger _logger;
    private readonly string _redisKey;

    public RedisScraperConfigStorage(string connectionString, string redisKey, ILogger logger) : base(connectionString)
    {
        _redisKey = redisKey;
        _logger = logger;
    }

    public async Task CreateConfigAsync(ScraperConfig config)
    {
        var db = Redis!.GetDatabase();

        await db.StringSetAsync(_redisKey, SerializeToJson(config));
    }

    public async Task<ScraperConfig> GetConfigAsync()
    {
        _logger.LogInformation("Start {method}", nameof(GetConfigAsync));
        var db = Redis!.GetDatabase();

        _logger.LogInformation("Checking if {key} exists in Redis", _redisKey);

        var keyExists = await db.KeyExistsAsync(_redisKey);

        if (!keyExists) return null;

        _logger.LogInformation("Getting scraper config by key {key} from Redis", _redisKey);

        var json = await db.StringGetAsync(_redisKey);

        _logger.LogInformation("Deserializing json string to scraper config");

        var result = JsonConvert.DeserializeObject<ScraperConfig>(json.ToString());
        return result;
    }
}