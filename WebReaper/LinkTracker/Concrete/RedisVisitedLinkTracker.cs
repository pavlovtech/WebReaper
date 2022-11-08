using StackExchange.Redis;
using WebReaper.LinkTracker.Abstract;

namespace WebReaper.LinkTracker.Concrete;

public class RedisVisitedLinkTracker : IVisitedLinkTracker
{
    private static ConnectionMultiplexer? redis;

    public RedisVisitedLinkTracker(string connectionString)
    {
        redis = ConnectionMultiplexer.Connect(connectionString, config =>
        {
            config.AbortOnConnectFail = false;

            config.AsyncTimeout = 180000;
            config.SyncTimeout = 180000;

            config.ReconnectRetryPolicy = new ExponentialRetry(10000);
        });
    }

    public async Task AddVisitedLinkAsync(string siteId, string visitedLink)
    {
        IDatabase db = redis!.GetDatabase();
        await db.SetAddAsync(siteId, visitedLink);
    }

    public async Task<List<string>> GetVisitedLinksAsync(string siteId)
    {
        IDatabase db = redis!.GetDatabase();
        var result = await db.SetMembersAsync(siteId);

        return result.Select(x => x.ToString()).ToList();
    }

    public Task<List<string>> GetNotVisitedLinks(string siteId, IEnumerable<string> links)
    {
        IDatabase db = redis!.GetDatabase();
        var result = links.Where(x => !db.SetContains(siteId, x));

        return Task.FromResult(result.ToList());
    }

    public async Task<long> GetVisitedLinksCount(string siteId)
    {
        IDatabase db = redis!.GetDatabase();
        return await db.SetLengthAsync(siteId);
    }
}