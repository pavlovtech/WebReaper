using StackExchange.Redis;
using WebReaper.LinkTracker.Abstract;

namespace WebReaper.LinkTracker;

public class RedisLinkTracker : ILinkTracker
{
    static readonly ConnectionMultiplexer redis = ConnectionMultiplexer.Connect("webreaper.redis.cache.windows.net:6380,password=AIWM15Q0XAKjfZYUc9ickXfwi8O3Ti9UFAzCaAnMeEc=,ssl=True,abortConnect=False");

    public async Task AddVisitedLink(string siteUrl, string visitedLink)
    {
        IDatabase db = redis.GetDatabase();
        await db.SetAddAsync(siteUrl, visitedLink);
    }

    public async Task<IEnumerable<string>> GetVisitedLinks(string siteUrl)
    {
        IDatabase db = redis.GetDatabase();
        var result = await db.SetMembersAsync(siteUrl);

        return result.Select(x => x.ToString());
    }
}