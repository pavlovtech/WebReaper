using StackExchange.Redis;
using WebReaper.Core.LinkTracker.Abstract;

namespace WebReaper.Redis;

public class RedisVisitedLinkTracker : IVisitedLinkTracker
{
    private readonly IDatabase _db;
    private readonly string _redisKey;

    public bool DataCleanupOnStart { get; set; }

    public Task Initialization { get; }

    public RedisVisitedLinkTracker(string connectionString, string redisKey, bool dataCleanupOnStart = false)
    {
        _db = RedisConnectionPool.GetDatabase(connectionString);
        _redisKey = redisKey;
        DataCleanupOnStart = dataCleanupOnStart;
        
        Initialization = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        if (!DataCleanupOnStart)
            return;

        var db = _db;

        await db.KeyDeleteAsync(_redisKey);
    }
    
    public async Task AddVisitedLinkAsync(string visitedLink)
    {
        var db = _db;
        await db.SetAddAsync(_redisKey, visitedLink);
    }

    public async Task<List<string>> GetVisitedLinksAsync()
    {
        var db = _db;
        var result = await db.SetMembersAsync(_redisKey);

        return result.Select(x => x.ToString()).ToList();
    }

    public Task<List<string>> GetNotVisitedLinks(IEnumerable<string> links)
    {
        var db = _db;
        var result = links.Where(x => !db.SetContains(_redisKey, x));

        return Task.FromResult(result.ToList());
    }

    public async Task<long> GetVisitedLinksCount()
    {
        var db = _db;
        return await db.SetLengthAsync(_redisKey);
    }
}