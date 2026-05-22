using StackExchange.Redis;
using WebReaper.Core.LinkTracker.Abstract;
using WebReaper.Infra.Abstract;

namespace WebReaper.Redis;

public class RedisVisitedLinkTracker : IVisitedLinkTracker, IAsyncInitializable
{
    private readonly IDatabase _db;
    private readonly string _redisKey;

    public bool DataCleanupOnStart { get; set; }

    private readonly Lazy<Task> _initialization;

    public RedisVisitedLinkTracker(string connectionString, string redisKey, bool dataCleanupOnStart = false)
    {
        _db = RedisConnectionPool.GetDatabase(connectionString);
        _redisKey = redisKey;
        DataCleanupOnStart = dataCleanupOnStart;
        
        _initialization = new Lazy<Task>(InitializeCoreAsync);
    }

    // ADR-0033: idempotent async warm-up, driven once before the crawl.
    public Task InitializeAsync() => _initialization.Value;

    private async Task InitializeCoreAsync()
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

    // ADR-0022 slice 3: the distributed idempotency authority. Redis SADD is
    // atomic and returns true iff the member was newly added — an exact
    // test-and-set, no Lua needed. Overrides the seam's non-atomic
    // check-then-add default so a redelivered/duplicate Job loses the race
    // and the Outstanding-work latch stays balanced across workers.
    public async Task<bool> TryAddVisitedLinkAsync(string visitedLink)
    {
        return await _db.SetAddAsync(_redisKey, visitedLink);
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