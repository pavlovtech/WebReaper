using WebReaper.Core.Crawling.Concrete;

namespace WebReaper.UnitTests;

// ADR-0022 / ADR-0032: the Outstanding-work latch (in-memory adapter) is an
// exact unit-credit termination detector — it trips exactly once, when
// outstanding credit reaches zero. SignalProcessedAsync(childCount) credits a
// Job's discovered children and returns the Job's own unit in ONE atomic step
// (net childCount - 1), so credit conservation is structural — there is no
// two-call ordering for a caller to get wrong.
public class OutstandingWorkLatchTests
{
    [Fact]
    public async Task Trips_exactly_once_when_credit_reaches_zero()
    {
        var latch = new InMemoryOutstandingWorkLatch();
        await latch.SeedAsync(3);

        Assert.False(await latch.SignalProcessedAsync(0)); // 3 -> 2
        Assert.False(await latch.SignalProcessedAsync(0)); // 2 -> 1
        Assert.True(await latch.SignalProcessedAsync(0));   // 1 -> 0  (trip)
    }

    [Fact]
    public async Task Childful_registration_credits_and_returns_in_one_step_so_the_latch_cannot_trip_early()
    {
        var latch = new InMemoryOutstandingWorkLatch();
        await latch.SeedAsync(1); // the root job

        // Root done, having discovered 2 children: 2 credited, 1 returned, in
        // one atomic op — net +1, so 1 -> 2. The latch cannot trip here, and
        // no interleaving can observe a partially-credited count.
        Assert.False(await latch.SignalProcessedAsync(2));

        Assert.False(await latch.SignalProcessedAsync(0)); // child: 2 -> 1
        Assert.True(await latch.SignalProcessedAsync(0));   // child: 1 -> 0 (trip)
    }

    [Fact]
    public async Task A_job_that_discovers_more_work_than_itself_pushes_the_count_up()
    {
        var latch = new InMemoryOutstandingWorkLatch();
        await latch.SeedAsync(1);

        Assert.False(await latch.SignalProcessedAsync(5)); // net +4: 1 -> 5

        for (var i = 0; i < 4; i++)
            Assert.False(await latch.SignalProcessedAsync(0)); // 5 -> 1
        Assert.True(await latch.SignalProcessedAsync(0));       // 1 -> 0 (trip)
    }

    [Fact]
    public async Task Exactly_one_caller_trips_under_concurrency()
    {
        var latch = new InMemoryOutstandingWorkLatch();
        await latch.SeedAsync(500);

        var trips = await Task.WhenAll(Enumerable.Range(0, 500)
            .Select(_ => Task.Run(() => latch.SignalProcessedAsync(0))));

        Assert.Equal(1, trips.Count(t => t));
    }
}
