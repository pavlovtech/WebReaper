using System.Collections.Immutable;
using System.Text.Json.Nodes;
using WebReaper.Core.Crawling;
using WebReaper.Domain;
using WebReaper.Sinks.Models;

namespace WebReaper.UnitTests;

// ADR-0081 (sibling to PageActionArmCensusTests): the CrawlOutcome closed sum
// is interpreted by every Crawl driver, and C# has no closed-hierarchy
// exhaustiveness to make a forgotten arm a compile error. This census is the
// cheap tripwire: when an arm is added or removed it fails with a checklist of
// every consumer to update (the in-process driver, the distributed-driver
// example, and the NextJobs projection).
public class CrawlOutcomeArmCensusTests
{
    [Fact]
    public void CrawlOutcome_arm_set_is_pinned_so_a_new_arm_forces_updating_every_consumer()
    {
        var arms = typeof(CrawlOutcome).GetNestedTypes()
            .Where(t => t.IsSealed && t.IsSubclassOf(typeof(CrawlOutcome)))
            .Select(t => t.Name)
            .ToHashSet();

        var expected = new HashSet<string>
        {
            nameof(CrawlOutcome.Parsed),
            nameof(CrawlOutcome.Followed),
            nameof(CrawlOutcome.Paginated),
            nameof(CrawlOutcome.Swept),
        };

        Assert.True(arms.SetEquals(expected),
            "CrawlOutcome's arm set changed. An arm is interpreted by every Crawl " +
            "driver, so update ALL of them, then this census:\n" +
            "  - WebReaper.Core.ScraperEngine (the in-process driver's arm switch)\n" +
            "  - Examples/WebReaper.AzureFuncs/WebReaperSpider.cs (the distributed driver)\n" +
            "  - CrawlOutcome.NextJobs (the candidate-children projection)\n" +
            $"Expected: [{string.Join(", ", expected.OrderBy(x => x))}]\n" +
            $"Actual:   [{string.Join(", ", arms.OrderBy(x => x))}]");
    }

    [Fact]
    public void Swept_carries_the_page_record_and_its_on_domain_children()
    {
        var data = new ParsedData("https://x.test/", new JsonObject());
        var child = new Job("https://x.test/a",
            ImmutableQueue<WebReaper.Domain.Selectors.LinkPathSelector>.Empty,
            ImmutableQueue<string>.Empty);

        var outcome = CrawlOutcome.Sweep(data, ImmutableArray.Create(child));

        var swept = Assert.IsType<CrawlOutcome.Swept>(outcome);
        Assert.Same(data, swept.Data);
        Assert.Equal(new[] { "https://x.test/a" }, swept.Next.Select(j => j.Url));
        // NextJobs surfaces the swept children too; a generic consumer that
        // enqueues NextJobs (the distributed else-branch) still follows them.
        Assert.Equal(new[] { "https://x.test/a" }, outcome.NextJobs.Select(j => j.Url));
    }
}
