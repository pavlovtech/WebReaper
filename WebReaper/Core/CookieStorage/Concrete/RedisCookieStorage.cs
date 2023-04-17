using System.Net;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using WebReaper.Core.CookieStorage.Abstract;
using WebReaper.DataAccess;

namespace WebReaper.Core.CookieStorage.Concrete;

public class RedisCookieStorage : RedisBase, ICookiesStorage
{
    private readonly ILogger _logger;
    private readonly string _redisKey;

    public RedisCookieStorage(string connectionString, string redisKey, ILogger logger) : base(connectionString)
    {
        _redisKey = redisKey;
        _logger = logger;
    }

    public async Task AddAsync(CookieContainer cookieContainer)
    {
        var db = Redis!.GetDatabase();

        await db.StringSetAsync(_redisKey, SerializeToJson(cookieContainer));
    }

    public async Task<CookieContainer> GetAsync()
    {
        var db = Redis!.GetDatabase();
        var json = await db.StringGetAsync(_redisKey);

        var result = JsonConvert.DeserializeObject<CookieContainer>(json.ToString());
        return result;
    }
}