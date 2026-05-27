using System.Collections.Immutable;
using System.Text.Json;
using WebReaper.Domain;
using WebReaper.Domain.PageActions;
using WebReaper.Domain.Selectors;
using WebReaper.Serialization;

namespace WebReaper.UnitTests;

// ADR 0008 step 2: the serialization grammar is System.Text.Json source-gen +
// converters, replacing Newtonsoft TypeNameHandling. These pin the two payloads
// that carry polymorphic members (the PageAction closed sum — ADR-0035 — and
// the ImmutableQueue selector chain): ScraperConfig (the ADR-0003 config
// payload) and Job (the ADR-0005 RedisScheduler asymmetry, closed here).
public class StjSerializationTests
{
    [Fact]
    public void Job_round_trips_with_type_fidelity()
    {
        // ADR 0005's named-but-unfixed asymmetry: a Job's ImmutableQueue
        // selector chain and PageAction arms lost type metadata with
        // TypeNameHandling.None. STJ + converters closes it.
        var job = new Job(
            "https://x.test/p",
            ImmutableQueue.CreateRange(new[]
            {
                new LinkPathSelector("a.cat", null, PageType.Static),
                new LinkPathSelector("a.item", "a.next", PageType.Dynamic)
            }),
            ImmutableQueue.CreateRange(new[] { "https://x.test", "https://x.test/c" }),
            PageType.Dynamic,
            new List<PageAction> { new PageAction.WaitForSelector("button#go", 42) });

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

        var pa = Assert.IsType<PageAction.WaitForSelector>(got.PageActions![0]);
        Assert.Equal("button#go", pa.Selector);
        Assert.Equal(42, pa.TimeoutMs);
    }

    [Fact]
    public void Press_arm_round_trips_through_job_serialization()
    {
        // ADR-0074: codec round-trip for the Press arm (wire tag "press", single
        // "key" field). Typed-field equality confirms both write + read paths.
        var job = new Job(
            "https://x.test/p",
            ImmutableQueue.CreateRange(new[]
            {
                new LinkPathSelector("a.item", null, PageType.Static)
            }),
            ImmutableQueue<string>.Empty,
            PageType.Static,
            new List<PageAction> { new PageAction.Press("Control+A") });

        var json = WebReaperJson.SerializeJob(job);
        var got = WebReaperJson.DeserializeJob(json);

        var pa = Assert.IsType<PageAction.Press>(got.PageActions![0]);
        Assert.Equal("Control+A", pa.Key);
    }

    [Fact]
    public void ScrollIntoView_arm_round_trips_with_typed_field_equality()
    {
        // ADR-0074: the new scrollIntoView arm must survive the codec
        // (write then read) with selector fidelity; this pins the wire
        // tag ("scrollIntoView") and the single required field ("selector").
        var job = new Job(
            "https://x.test/p",
            ImmutableQueue.CreateRange(new[]
            {
                new LinkPathSelector("a.item", null, PageType.Static)
            }),
            ImmutableQueue<string>.Empty,
            PageType.Static,
            new List<PageAction> { new PageAction.ScrollIntoView("#lazy-section") });

        var json = WebReaperJson.SerializeJob(job);
        var got = WebReaperJson.DeserializeJob(json);

        var arm = Assert.IsType<PageAction.ScrollIntoView>(got.PageActions![0]);
        Assert.Equal("#lazy-section", arm.Selector);
    }

    [Fact]
    public void DeserializeJob_throws_on_a_chain_entry_with_a_blank_selector()
    {
        // ADR-0030: a corrupt persisted Job — a selector-chain entry whose
        // 'selector' is blank — fails fast at the codec (queue-read) with the
        // JSON property name, not late at the Crawl step.
        var job = new Job(
            "https://x.test/p",
            ImmutableQueue.CreateRange(new[] { new LinkPathSelector("sentinel-sel") }),
            ImmutableQueue<string>.Empty);
        var json = WebReaperJson.SerializeJob(job);

        // Tamper: blank out the only selector value.
        var tampered = json.Replace("sentinel-sel", "");

        Assert.Throws<JsonException>(() => WebReaperJson.DeserializeJob(tampered));
    }
}
