using System.Collections.Immutable;
using System.Net;
using WebReaper.ConfigStorage.Concrete;
using WebReaper.Core.CookieStorage.Concrete;
using WebReaper.DataAccess;
using WebReaper.Domain;
using WebReaper.Domain.PageActions;
using WebReaper.Domain.Selectors;
using WebReaper.Exceptions;

namespace WebReaper.UnitTests;

// The payload-quirk half of ADR 0003, tested at the shell interface over an
// in-memory blob store. These pin the two things that must survive the
// deepening: the config shell applies TypeNameHandling.Auto *symmetrically*
// (the selector chain + PageActions round-trip — Redis was lossy with
// None, the file adapter serialized Auto but deserialized with defaults),
// and the cookie shell quarantines the CookieContainer↔CookieCollection
// mapping. The missing-value policy is uniform per shell.
public class PayloadShellTests
{
    [Fact]
    public async Task Config_shell_round_trips_selector_chain_and_page_actions()
    {
        var chain = ImmutableQueue.CreateRange(new[]
        {
            new LinkPathSelector("a.cat", null, PageType.Static),
            new LinkPathSelector("a.item", "a.next", PageType.Dynamic,
                new List<PageAction> { new PageAction.WaitForSelector("div.loaded", 5000) })
        });

        var config = new ScraperConfig(
            ParsingScheme: null,
            LinkPathSelectors: chain,
            StartUrls: new[] { "https://x.test/start" },
            UrlBlackList: new[] { "https://x.test/skip" },
            PageCrawlLimit: 123,
            StartPageType: PageType.Dynamic,
            PageActions: new List<PageAction> { new PageAction.Click("button#go") },
            Headless: false,
            StopWhenDrained: true);

        var store = new ScraperConfigStore(new InMemoryBlobStore(), "k");
        await store.CreateConfigAsync(config);
        var got = await store.GetConfigAsync();

        Assert.Equal(new[] { "https://x.test/start" }, got.StartUrls);
        Assert.Equal(new[] { "https://x.test/skip" }, got.UrlBlackList);
        Assert.Equal(123, got.PageCrawlLimit);
        Assert.Equal(PageType.Dynamic, got.StartPageType);
        Assert.False(got.Headless);
        Assert.True(got.StopWhenDrained);

        var selectors = got.LinkPathSelectors.ToArray();
        Assert.Equal(2, selectors.Length);
        Assert.Equal("a.cat", selectors[0].Selector);
        Assert.Equal("a.next", selectors[1].PaginationSelector);
        Assert.Equal(PageType.Dynamic, selectors[1].PageType);
        var wfs = Assert.IsType<PageAction.WaitForSelector>(selectors[1].PageActions![0]);
        Assert.Equal(5000, wfs.TimeoutMs);

        Assert.NotNull(got.PageActions);
        Assert.Single(got.PageActions!);
        var pa = Assert.IsType<PageAction.Click>(got.PageActions![0]);
        Assert.Equal("button#go", pa.Selector);
    }

    [Fact]
    public async Task Config_shell_round_trips_Press_arm()
    {
        // ADR-0074: the Press arm must survive the ScraperConfig payload shell
        // (InMemoryBlobStore round-trip). Single "key" field, no selector.
        var config = new ScraperConfig(
            ParsingScheme: null,
            LinkPathSelectors: ImmutableQueue<LinkPathSelector>.Empty,
            StartUrls: new[] { "https://x.test/start" },
            UrlBlackList: Array.Empty<string>(),
            PageCrawlLimit: 1,
            StartPageType: PageType.Static,
            PageActions: new List<PageAction> { new PageAction.Press("Enter") },
            Headless: true,
            StopWhenDrained: false);

        var store = new ScraperConfigStore(new InMemoryBlobStore(), "press-key");
        await store.CreateConfigAsync(config);
        var got = await store.GetConfigAsync();

        Assert.NotNull(got.PageActions);
        Assert.Single(got.PageActions!);
        var pa = Assert.IsType<PageAction.Press>(got.PageActions![0]);
        Assert.Equal("Enter", pa.Key);
    }

    [Fact]
    public async Task Config_shell_round_trips_scroll_into_view_arm()
    {
        // ADR-0074: a ScraperConfig carrying a ScrollIntoView arm must
        // survive the payload-shell round-trip (serialize, store, retrieve,
        // deserialize) with selector fidelity. Covers BOTH top-level
        // PageActions AND chain-nested LinkPathSelector.PageActions.
        var chain = ImmutableQueue.CreateRange(new[]
        {
            new LinkPathSelector("a.item", null, PageType.Dynamic,
                new List<PageAction> { new PageAction.ScrollIntoView(".lazy-card") })
        });

        var config = new ScraperConfig(
            ParsingScheme: null,
            LinkPathSelectors: chain,
            StartUrls: new[] { "https://x.test/start" },
            UrlBlackList: Array.Empty<string>(),
            PageCrawlLimit: int.MaxValue,
            StartPageType: PageType.Dynamic,
            PageActions: new List<PageAction> { new PageAction.ScrollIntoView("#hero") },
            Headless: true,
            StopWhenDrained: false);

        var store = new ScraperConfigStore(new InMemoryBlobStore(), "k2");
        await store.CreateConfigAsync(config);
        var got = await store.GetConfigAsync();

        var topArm = Assert.IsType<PageAction.ScrollIntoView>(got.PageActions![0]);
        Assert.Equal("#hero", topArm.Selector);

        var chainArm = Assert.IsType<PageAction.ScrollIntoView>(
            got.LinkPathSelectors.Single().PageActions![0]);
        Assert.Equal(".lazy-card", chainArm.Selector);
    }

    [Fact]
    public async Task Config_shell_throws_typed_not_found_when_absent()
    {
        var store = new ScraperConfigStore(new InMemoryBlobStore(), "missing");
        await Assert.ThrowsAsync<ConfigNotFoundException>(() => store.GetConfigAsync());
    }

    [Fact]
    public async Task Cookie_shell_round_trips_cookies_across_domains()
    {
        var container = new CookieContainer();
        container.Add(new Cookie("a", "1", "/", "x.test"));
        container.Add(new Cookie("b", "2", "/", "y.test"));

        var store = new CookieStore(new InMemoryBlobStore(), "k");
        await store.AddAsync(container);
        var got = (await store.GetAsync()).GetAllCookies();

        var pairs = got.Select(c => (c.Name, c.Value)).OrderBy(p => p.Name).ToArray();
        Assert.Equal(new[] { ("a", "1"), ("b", "2") }, pairs);
    }

    [Fact]
    public async Task Cookie_shell_returns_empty_container_when_absent()
    {
        var store = new CookieStore(new InMemoryBlobStore(), "none");
        var got = await store.GetAsync();

        Assert.NotNull(got);
        Assert.Empty(got.GetAllCookies());
    }
}
