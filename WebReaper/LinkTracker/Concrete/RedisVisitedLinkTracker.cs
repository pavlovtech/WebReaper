using StackExchange.Redis;
using WebReaper.LinkTracker.Abstract;

namespace WebReaper.LinkTracker.Concrete;

public class RedisVisitedLinkTracker : IVisitedLinkTracker
{
    private readonly string _redisKey;
    private static ConnectionMultiplexer? redis;

    public RedisVisitedLinkTracker(string connectionString, string redisKey)
    {
        _redisKey = redisKey;
        redis = ConnectionMultiplexer.Connect(connectionString, config =>
        {
            config.AbortOnConnectFail = false;

            config.AsyncTimeout = 180000;
            config.SyncTimeout = 180000;

            config.ReconnectRetryPolicy = new ExponentialRetry(10000);
        });
    }

    public async Task AddVisitedLinkAsync(string visitedLink)
    {
        IDatabase db = redis!.GetDatabase();
        await db.SetAddAsync(_redisKey, visitedLink);
    }

    public async Task<List<string>> GetVisitedLinksAsync()
    {
        IDatabase db = redis!.GetDatabase();
        var result = await db.SetMembersAsync(_redisKey);

        return result.Select(x => x.ToString()).ToList();
    }

    public Task<List<string>> GetNotVisitedLinks(IEnumerable<string> links)
    {
        IDatabase db = redis!.GetDatabase();
        var result = links.Where(x => !db.SetContains(_redisKey, x));

        return Task.FromResult(result.ToList());
    }

    public async Task<long> GetVisitedLinksCount()
    {
        IDatabase db = redis!.GetDatabase();
        return await db.SetLengthAsync(_redisKey);
    }
}