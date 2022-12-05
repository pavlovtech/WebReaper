using System.Net;
using Exoscan.CookieStorage.Abstract;
using Exoscan.DataAccess;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace Exoscan.CookieStorage.Concrete;

public class RedisCookieStorage: RedisBase, ICookiesStorage
{
    private readonly string _redisKey;
    private readonly ILogger _logger;

    public RedisCookieStorage(string connectionString, string redisKey, ILogger logger): base(connectionString)
    {
        _redisKey = redisKey;
        _logger = logger;
    }
    
    public async Task AddAsync(CookieContainer cookieContainer)
    {
        IDatabase db = Redis!.GetDatabase();

        await db.StringSetAsync(_redisKey, SerializeToJson(cookieContainer));
    }

    public async Task<CookieContainer> GetAsync()
    {
        IDatabase db = Redis!.GetDatabase();
        var json = await db.StringGetAsync(_redisKey);

        var result = JsonConvert.DeserializeObject<CookieContainer>(json.ToString());
        return result;
    }
}