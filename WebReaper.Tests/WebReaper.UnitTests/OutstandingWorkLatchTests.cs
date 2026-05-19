using WebReaper.Core.Crawling.Concrete;

namespace WebReaper.UnitTests;

// ADR-0022 slice 3: the Outstanding-work latch (in-memory adapter) is an
// exact unit-credit termination detector — it trips exactly once, when
// outstanding credit reaches zero, and children credited before a parent's
// unit is returned cannot let it trip early (the credit-conservation
// precondition the research pins as load-bearing).
public class OutstandingWorkLatchTests
{
    [Fact]
    public async Task Trips_exactly_once_when_credit_reaches_zero()
    {
        var latch = new InMemoryOutstandingWorkLatch();
        await latch.SeedAsync(3);

        Assert.False(await latch.SignalProcessedAsync()); // 3 -> 2
        Assert.False(await latch.SignalProcessedAsync()); // 2 -> 1
        Assert.True(await latch.SignalProcessedAsync());   // 1 -> 0  (trip)
    }

    [Fact]
    public async Task Children_credited_before_parent_unit_returned_prevents_early_trip()
    {
        var latch = new InMemoryOutstandingWorkLatch();
        await latch.SeedAsync(1);          // the root job

        await latch.AddAsync(2);           // root discovered 2 children (credited first)
        Assert.False(await latch.SignalProcessedAsync()); // root done: 3 -> 2 (NOT zero)

        Assert.False(await latch.SignalProcessedAsync()); // child: 2 -> 1
        Assert.True(await latch.SignalProcessedAsync());   // child: 1 -> 0 (trip)
    }

    [Fact]
    public async Task Exactly_one_caller_trips_under_concurrency()
    {
        var latch = new InMemoryOutstandingWorkLatch();
        await latch.SeedAsync(500);

        var trips = await Task.WhenAll(Enumerable.Range(0, 500)
            .Select(_ => Task.Run(() => latch.SignalProcessedAsync())));

        Assert.Equal(1, trips.Count(t => t));
    }
}
