using WebReaper.Core.LinkTracker.Concrete;

namespace WebReaper.UnitTests;

// ADR-0022 slice 2: the idempotency authority. InMemoryVisitedLinkTracker's
// TryAddVisitedLinkAsync must be an exact atomic test-and-set — under
// concurrency exactly one caller wins for a given URL. This is what makes
// discovery dedup and Outstanding-work-latch accounting correct without
// relying on at-least-once queue semantics (the seam's check-then-add default
// has a race window; this override does not).
public class VisitedLinkTrackerTests
{
    [Fact]
    public async Task TryAdd_is_an_atomic_test_and_set_under_concurrency()
    {
        var tracker = new InMemoryVisitedLinkTracker();

        // 200 tasks race to add the SAME url; exactly one must win.
        var results = await Task.WhenAll(Enumerable.Range(0, 200)
            .Select(_ => Task.Run(() => tracker.TryAddVisitedLinkAsync("https://x.test/same"))));

        Assert.Equal(1, results.Count(won => won));
        Assert.Equal(1, await tracker.GetVisitedLinksCount());
    }

    [Fact]
    public async Task TryAdd_returns_true_once_per_distinct_url_then_false()
    {
        var tracker = new InMemoryVisitedLinkTracker();

        Assert.True(await tracker.TryAddVisitedLinkAsync("a"));
        Assert.False(await tracker.TryAddVisitedLinkAsync("a"));   // already present
        Assert.True(await tracker.TryAddVisitedLinkAsync("b"));
        Assert.Equal(2, await tracker.GetVisitedLinksCount());
    }
}
