using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Core.Crawling.Concrete;
using WebReaper.Core.LinkTracker.Concrete;

namespace WebReaper.UnitTests;

// ADR-0032: the stop rule is the in-process Crawl driver's one home for
// "should this Crawl stop, and why?" — it composes the Outstanding-work latch
// (completion) and the soft page limit (cutoff). Because it is a module, its
// verdict is assertable offline here, without a running ScraperEngine.
public class StopRuleTests
{
    private static StopRule Rule(
        int pageCrawlLimit = int.MaxValue,
        bool stopWhenDrained = false,
        InMemoryVisitedLinkTracker? tracker = null)
        => new(
            new InMemoryOutstandingWorkLatch(),
            tracker ?? new InMemoryVisitedLinkTracker(),
            pageCrawlLimit,
            stopWhenDrained,
            NullLogger.Instance);

    [Fact]
    public async Task Limit_zero_concludes_at_seed()
    {
        var rule = Rule(pageCrawlLimit: 0);

        await rule.SeedAsync(1);

        Assert.True(rule.IsCrawlOver); // 0 pages visited already meets a limit of 0
    }

    [Fact]
    public async Task Empty_start_under_stop_when_drained_concludes_at_seed()
    {
        var rule = Rule(stopWhenDrained: true);

        await rule.SeedAsync(0); // no start work to drain

        Assert.True(rule.IsCrawlOver);
    }

    [Fact]
    public async Task Drained_concludes_when_the_latch_reaches_zero()
    {
        var rule = Rule(stopWhenDrained: true);
        await rule.SeedAsync(1);

        Assert.False(rule.IsCrawlOver);
        Assert.False(await rule.RegisterProcessedAsync(2)); // root + 2 children: 1 -> 2
        Assert.False(await rule.RegisterProcessedAsync(0)); // child: 2 -> 1
        Assert.True(await rule.RegisterProcessedAsync(0));   // child: 1 -> 0 (drained)
        Assert.True(rule.IsCrawlOver);
    }

    [Fact]
    public async Task Page_limit_concludes_when_the_visited_count_reaches_it()
    {
        var tracker = new InMemoryVisitedLinkTracker();
        var rule = Rule(pageCrawlLimit: 2, tracker: tracker);
        await rule.SeedAsync(1);

        await tracker.AddVisitedLinkAsync("a");
        Assert.False(await rule.RegisterProcessedAsync(0)); // 1 visited < 2
        Assert.False(rule.IsCrawlOver);

        await tracker.AddVisitedLinkAsync("b");
        Assert.True(await rule.RegisterProcessedAsync(0));   // 2 visited >= 2 (cutoff)
        Assert.True(rule.IsCrawlOver);
    }

    [Fact]
    public async Task Neither_condition_set_never_concludes()
    {
        var rule = Rule(); // unbounded limit, StopWhenDrained off — the default crawl

        await rule.SeedAsync(1);
        Assert.False(await rule.RegisterProcessedAsync(0));
        Assert.False(await rule.RegisterProcessedAsync(0));
        Assert.False(rule.IsCrawlOver);
    }

    [Fact]
    public async Task Conclusion_is_reported_to_exactly_one_caller()
    {
        var tracker = new InMemoryVisitedLinkTracker();
        var rule = Rule(pageCrawlLimit: 1, tracker: tracker);
        await rule.SeedAsync(1);
        await tracker.AddVisitedLinkAsync("a"); // already at the limit

        Assert.True(await rule.RegisterProcessedAsync(0));  // the one caller told to act
        Assert.False(await rule.RegisterProcessedAsync(0)); // still over, but not re-reported
        Assert.False(await rule.RegisterProcessedAsync(0));
        Assert.True(rule.IsCrawlOver);
    }

    [Fact]
    public async Task Page_limit_concludes_before_the_latch_drains_when_it_is_the_smaller_bound()
    {
        var tracker = new InMemoryVisitedLinkTracker();
        var rule = Rule(pageCrawlLimit: 1, stopWhenDrained: true, tracker: tracker);
        await rule.SeedAsync(1);

        // The root is processed and discovers 3 more pages — the latch is far
        // from drained (credit 3) — but the visited count has reached the
        // limit, so the cutoff concludes the Crawl.
        await tracker.AddVisitedLinkAsync("root");
        Assert.True(await rule.RegisterProcessedAsync(3));
        Assert.True(rule.IsCrawlOver);
    }
}
