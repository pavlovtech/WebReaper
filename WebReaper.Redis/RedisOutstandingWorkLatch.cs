using StackExchange.Redis;
using WebReaper.Core.Crawling.Abstract;

namespace WebReaper.Redis;

/// <summary>
/// Distributed Outstanding-work latch (ADR-0022; ADR-0032): an atomic Redis
/// counter over the one shared ADR-0005 <see cref="RedisConnectionPool"/>,
/// plus a SET-NX completion fence so the one-shot end-of-crawl fires
/// <b>exactly once</b> even under Service Bus at-least-once redelivery.
///
/// Correctness does not depend on the queue's delivery semantics: it rides on
/// the idempotency authority (<see cref="RedisVisitedLinkTracker"/>'s atomic
/// <c>SADD</c> test-and-set) — the distributed Crawl driver only signals
/// genuinely-new work, so a redelivered Job is a no-op that does not unbalance
/// the counter. The single <c>INCRBY</c> of <c>childCount - 1</c> that
/// observes zero then races a single <c>SET completed NX</c>; only its winner
/// trips, so completion is run once.
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

    /// <summary>
    /// Credit the children and return this Job's unit in one atomic
    /// <c>INCRBY</c> of <c>childCount - 1</c> (one round-trip — replaces the
    /// former INCRBY + DECRBY pair); when it observes zero, races a single
    /// <c>SET completed NX</c> so the one-shot trips exactly once.
    /// </summary>
    public async Task<bool> SignalProcessedAsync(int childCount)
    {
        var remaining = await _db.StringIncrementAsync(_counterKey, childCount - 1);
        if (remaining > 0) return false;

        // Outstanding credit hit zero — the Crawl is terminated. CAS-fence the
        // one-shot: only the first caller to claim the completion key returns
        // true, so an at-least-once redelivered zero cannot double-fire the
        // end-of-crawl action (research: exactly-once *effect*, not delivery).
        return await _db.StringSetAsync(_completionKey, "1", when: When.NotExists);
    }
}
