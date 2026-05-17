using System.Collections.Immutable;
using WebReaper.Domain;
using WebReaper.Domain.PageActions;
using WebReaper.Domain.Selectors;
using WebReaper.Serialization;

namespace WebReaper.UnitTests;

// ADR 0008 step 2: the serialization grammar is System.Text.Json source-gen +
// converters, replacing Newtonsoft TypeNameHandling. These pin the two payloads
// that carry polymorphic members (object[] PageAction.Parameters, the
// ImmutableQueue selector chain): ScraperConfig (the ADR-0003 config payload)
// and Job (the ADR-0005 RedisScheduler asymmetry, closed here).
public class StjSerializationTests
{
    [Fact]
    public void Job_round_trips_with_type_fidelity()
    {
        // ADR 0005's named-but-unfixed asymmetry: a Job's ImmutableQueue
        // selector chain and object[] PageAction.Parameters lost type metadata
        // with TypeNameHandling.None. STJ + converters closes it.
        var job = new Job(
            "https://x.test/p",
            ImmutableQueue.CreateRange(new[]
            {
                new LinkPathSelector("a.cat", null, PageType.Static),
                new LinkPathSelector("a.item", "a.next", PageType.Dynamic)
            }),
            ImmutableQueue.CreateRange(new[] { "https://x.test", "https://x.test/c" }),
            PageType.Dynamic,
            new List<PageAction> { new(PageActionType.Click, "button#go", 42) });

        var json = WebReaperJson.SerializeJob(job);
        var got = WebReaperJson.DeserializeJob(json);

        Assert.Equal("https://x.test/p", got.Url);
        Assert.Equal(PageType.Dynamic, got.PageType);

        var chain = got.LinkPathSelectors.ToArray();
        Assert.Equal(2, chain.Length);
        Assert.Equal("a.cat", chain[0].Selector);
        Assert.Equal("a.next", chain[1].PaginationSelector);
        Assert.Equal(PageType.Dynamic, chain[1].PageType);

        Assert.Equal(new[] { "https://x.test", "https://x.test/c" },
            got.ParentBacklinks.ToArray());

        Assert.Equal(PageActionType.Click, got.PageActions![0].Type);
        Assert.Equal("button#go", got.PageActions![0].Parameters[0].ToString());
        Assert.Equal(42, Convert.ToInt32(got.PageActions![0].Parameters[1]));
    }
}
