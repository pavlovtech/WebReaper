using StackExchange.Redis;
using WebReaper.Core.Crawling.Abstract;

namespace WebReaper.Redis;

/// <summary>
/// Distributed Outstanding-work latch (ADR-0022 slice 3): an atomic Redis
/// counter (INCRBY / DECRBY) over the one shared ADR-0005
/// <see cref="RedisConnectionPool"/>, plus a SET-NX completion fence so the
/// one-shot end-of-crawl fires <b>exactly once</b> even under Service Bus
/// at-least-once redelivery.
///
/// Correctness does not depend on the queue's delivery semantics: it rides on
/// the idempotency authority (<see cref="RedisVisitedLinkTracker"/>'s atomic
/// <c>SADD</c> test-and-set) — the distributed Crawl driver only credits /
/// returns genuinely-new work, so a redelivered Job is a no-op that neither
/// double-credits nor double-decrements the counter. The
/// <c>StringDecrement</c> that observes zero then races a single
/// <c>SET completed NX</c>; only its winner trips, so completion is run once.
/// </summary>
public sealed class RedisOutstandingWorkLatch : IOutstandingWorkLatch
{
    private readonly IDatabase _db;
    private readonly string _counterKey;
    private readonly string _completionKey;

    /// <param name="connectionString">Resolved through the shared ADR-0005
    /// pool (one multiplexer per connection string).</param>
    /// <param name="runKey">Identifies this Crawl's latch keyspace, so
    /// independent distributed crawls don't share a counter.</param>
    public RedisOutstandingWorkLatch(string connectionString, string runKey)
    {
        _db = RedisConnectionPool.GetDatabase(connectionString);
        _counterKey = $"{runKey}:outstanding";
        _completionKey = $"{runKey}:completed";
    }

    public async Task SeedAsync(int startJobCount)
    {
        // A fresh run starts from a clean keyspace.
        await _db.KeyDeleteAsync(_counterKey);
        await _db.KeyDeleteAsync(_completionKey);

        if (startJobCount > 0)
            await _db.StringIncrementAsync(_counterKey, startJobCount);
    }

    public Task AddAsync(int childCount)
        => childCount == 0
            ? Task.CompletedTask
            : _db.StringIncrementAsync(_counterKey, childCount);

    public async Task<bool> SignalProcessedAsync()
    {
        var remaining = await _db.StringDecrementAsync(_counterKey);
        if (remaining > 0) return false;

        // Outstanding credit hit zero — the Crawl is terminated. CAS-fence the
        // one-shot: only the first caller to claim the completion key returns
        // true, so an at-least-once redelivered zero cannot double-fire the
        // end-of-crawl action (research: exactly-once *effect*, not delivery).
        return await _db.StringSetAsync(_completionKey, "1", when: When.NotExists);
    }
}
