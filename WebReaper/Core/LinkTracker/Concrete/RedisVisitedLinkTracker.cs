using StackExchange.Redis;
using WebReaper.Core.LinkTracker.Abstract;
using WebReaper.DataAccess;

namespace WebReaper.Core.LinkTracker.Concrete;

public class RedisVisitedLinkTracker : RedisBase, IVisitedLinkTracker
{
    private readonly string _redisKey;

    public RedisVisitedLinkTracker(string connectionString, string redisKey): base(connectionString)
    {
        _redisKey = redisKey;
    }

    public async Task AddVisitedLinkAsync(string visitedLink)
    {
        IDatabase db = Redis!.GetDatabase();
        await db.SetAddAsync(_redisKey, visitedLink);
    }

    public async Task<List<string>> GetVisitedLinksAsync()
    {
        IDatabase db = Redis!.GetDatabase();
        var result = await db.SetMembersAsync(_redisKey);

        return result.Select(x => x.ToString()).ToList();
    }

    public Task<List<string>> GetNotVisitedLinks(IEnumerable<string> links)
    {
        IDatabase db = Redis!.GetDatabase();
        var result = links.Where(x => !db.SetContains(_redisKey, x));

        return Task.FromResult(result.ToList());
    }

    public async Task<long> GetVisitedLinksCount()
    {
        IDatabase db = Redis!.GetDatabase();
        return await db.SetLengthAsync(_redisKey);
    }
}