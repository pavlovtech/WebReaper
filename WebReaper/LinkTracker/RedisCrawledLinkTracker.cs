using StackExchange.Redis;
using WebReaper.LinkTracker.Abstract;

namespace WebReaper.LinkTracker;

public class RedisCrawledLinkTracker : ICrawledLinkTracker
{
    private static ConnectionMultiplexer? redis;

    public RedisCrawledLinkTracker(string connectionString)
    {
        redis = ConnectionMultiplexer.Connect(connectionString, config => {
            config.AbortOnConnectFail = false;

            config.AsyncTimeout = 180000;
            config.SyncTimeout = 180000;

            config.ReconnectRetryPolicy = new ExponentialRetry(10000);
        });
    }

    public async Task AddVisitedLinkAsync(string siteUrl, string visitedLink)
    {
        IDatabase db = redis!.GetDatabase();
        await db.SetAddAsync(siteUrl, visitedLink);
    }

    public async Task<IEnumerable<string>> GetVisitedLinksAsync(string siteUrl)
    {
        IDatabase db = redis!.GetDatabase();
        var result = await db.SetMembersAsync(siteUrl);

        return result.Select(x => x.ToString());
    }

    public Task<IEnumerable<string>> GetNotVisitedLinks(string siteUrl, IEnumerable<string> links)
    {
        IDatabase db = redis!.GetDatabase();
        var result = links.Where(x => !db.SetContains(siteUrl, x));
        
        return Task.FromResult(result);
    }

    public async Task<long> GetVisitedLinksCount(string siteUrl)
    {
        IDatabase db = redis!.GetDatabase();
        return await db.SetLengthAsync("visitedLinks");
    }
}