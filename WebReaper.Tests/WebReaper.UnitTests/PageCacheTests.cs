using WebReaper.Core.Loaders.Abstract;
using WebReaper.Core.Loaders.Concrete;
using WebReaper.Domain.Selectors;

namespace WebReaper.UnitTests;

// ADR-0041: the page-loader's cache-aside collaborator. These tests pin
// the seam's behaviour at the InMemoryPageCache adapter — the TTL
// semantics, the (url, pageType) keying, the TimeSpan.Zero
// "store-but-never-serve" mode, and the NullPageCache no-op default.
public class PageCacheTests
{
    [Fact]
    public async Task NullPageCache_always_misses_and_no_ops_writes()
    {
        // The default: preserves pre-0041 PageLoader behaviour exactly.
        IPageCache cache = new NullPageCache();

        await cache.WriteAsync("https://x.test/", PageType.Static, "<html/>", default);

        var got = await cache.TryReadAsync("https://x.test/", PageType.Static, default);

        Assert.Null(got);
    }

    [Fact]
    public async Task InMemoryPageCache_hit_serves_the_stored_document()
    {
        var cache = new InMemoryPageCache(TimeSpan.FromMinutes(10));

        await cache.WriteAsync("https://x.test/", PageType.Static, "<html>cached</html>", default);

        var got = await cache.TryReadAsync("https://x.test/", PageType.Static, default);

        Assert.Equal("<html>cached</html>", got);
    }

    [Fact]
    public async Task InMemoryPageCache_miss_returns_null()
    {
        var cache = new InMemoryPageCache(TimeSpan.FromMinutes(10));

        var got = await cache.TryReadAsync("https://x.test/never-stored", PageType.Static, default);

        Assert.Null(got);
    }

    [Fact]
    public async Task InMemoryPageCache_keys_pagetype_apart()
    {
        // Static and Dynamic loads of the same URL can produce different
        // HTML (server-rendered shell vs JS-rendered DOM). The cache key
        // must include PageType so a Static-cached entry never satisfies
        // a Dynamic request.
        var cache = new InMemoryPageCache(TimeSpan.FromMinutes(10));

        await cache.WriteAsync("https://x.test/", PageType.Static, "<static/>", default);

        var dynamicRead = await cache.TryReadAsync("https://x.test/", PageType.Dynamic, default);
        Assert.Null(dynamicRead);

        var staticRead = await cache.TryReadAsync("https://x.test/", PageType.Static, default);
        Assert.Equal("<static/>", staticRead);
    }

    [Fact]
    public async Task InMemoryPageCache_stale_entries_report_a_miss()
    {
        // TTL expiry: an entry older than maxAge reports as a miss.
        // The test seam takes a clock so we observe the boundary without
        // a Thread.Sleep.
        var now = DateTimeOffset.UtcNow;
        var cache = new InMemoryPageCache(TimeSpan.FromMinutes(10), () => now);

        await cache.WriteAsync("https://x.test/", PageType.Static, "<v1/>", default);

        now = now.AddMinutes(11);

        var got = await cache.TryReadAsync("https://x.test/", PageType.Static, default);

        Assert.Null(got);
    }

    [Fact]
    public async Task InMemoryPageCache_entry_within_ttl_is_a_hit()
    {
        // Boundary check the other way — exactly at the TTL is still
        // valid; just past is stale.
        var now = DateTimeOffset.UtcNow;
        var cache = new InMemoryPageCache(TimeSpan.FromMinutes(10), () => now);

        await cache.WriteAsync("https://x.test/", PageType.Static, "<v1/>", default);

        now = now.AddMinutes(10);

        var got = await cache.TryReadAsync("https://x.test/", PageType.Static, default);

        Assert.Equal("<v1/>", got);
    }

    [Fact]
    public async Task InMemoryPageCache_overwrites_entries_on_repeat_write()
    {
        // A second write to the same key overwrites; the new StoredAt
        // resets the TTL clock.
        var now = DateTimeOffset.UtcNow;
        var cache = new InMemoryPageCache(TimeSpan.FromMinutes(10), () => now);

        await cache.WriteAsync("https://x.test/", PageType.Static, "<v1/>", default);

        now = now.AddMinutes(5);
        await cache.WriteAsync("https://x.test/", PageType.Static, "<v2/>", default);

        // 6 minutes later — within the refreshed TTL.
        now = now.AddMinutes(6);
        var got = await cache.TryReadAsync("https://x.test/", PageType.Static, default);

        Assert.Equal("<v2/>", got);
    }

    [Fact]
    public async Task InMemoryPageCache_max_age_zero_stores_but_never_serves()
    {
        // ADR-0041: TimeSpan.Zero is the "force-fresh but snapshot"
        // mode. Writes succeed; reads always miss.
        var cache = new InMemoryPageCache(TimeSpan.Zero);

        await cache.WriteAsync("https://x.test/", PageType.Static, "<html/>", default);

        var got = await cache.TryReadAsync("https://x.test/", PageType.Static, default);

        Assert.Null(got);
    }

    [Fact]
    public void InMemoryPageCache_rejects_negative_max_age()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new InMemoryPageCache(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public async Task InMemoryPageCache_clear_drops_all_entries()
    {
        var cache = new InMemoryPageCache(TimeSpan.FromMinutes(10));

        await cache.WriteAsync("https://x.test/a", PageType.Static, "<a/>", default);
        await cache.WriteAsync("https://x.test/b", PageType.Dynamic, "<b/>", default);

        cache.Clear();

        Assert.Null(await cache.TryReadAsync("https://x.test/a", PageType.Static, default));
        Assert.Null(await cache.TryReadAsync("https://x.test/b", PageType.Dynamic, default));
    }

    [Fact]
    public async Task PageLoader_serves_cache_hit_without_calling_transport()
    {
        // The cache-aside flow in PageLoader: a hit returns without
        // touching the transport. Asserted via a transport that throws
        // if called — the test passes only because the cache short-
        // circuits the dispatch.
        var cache = new InMemoryPageCache(TimeSpan.FromMinutes(10));
        await cache.WriteAsync("https://x.test/", PageType.Static, "<cached/>", default);

        var loader = new PageLoader(
            new ThrowingTransport(),
            new ThrowingTransport(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            cache);

        var got = await loader.LoadAsync(new PageRequest("https://x.test/", PageType.Static, null, true), default);

        Assert.Equal("<cached/>", got);
    }

    [Fact]
    public async Task PageLoader_writes_to_cache_on_successful_transport_load()
    {
        // After a transport fetch, the document is stored — a second
        // LoadAsync for the same (url, pageType) serves from cache.
        var cache = new InMemoryPageCache(TimeSpan.FromMinutes(10));
        var http = new StubTransport("<fetched/>");

        var loader = new PageLoader(
            http,
            new ThrowingTransport(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            cache);

        var first = await loader.LoadAsync(new PageRequest("https://x.test/", PageType.Static, null, true), default);
        var second = await loader.LoadAsync(new PageRequest("https://x.test/", PageType.Static, null, true), default);

        Assert.Equal("<fetched/>", first);
        Assert.Equal("<fetched/>", second);
        Assert.Equal(1, http.CallCount);
    }

    [Fact]
    public async Task PageLoader_cache_write_failure_does_not_fail_the_load()
    {
        // ADR-0041: a cache write failure is logged and swallowed; the
        // successful load is still returned to the caller.
        var cache = new ThrowingWriteCache();
        var http = new StubTransport("<fetched/>");

        var loader = new PageLoader(
            http,
            new ThrowingTransport(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            cache);

        var got = await loader.LoadAsync(new PageRequest("https://x.test/", PageType.Static, null, true), default);

        Assert.Equal("<fetched/>", got);
    }

    private sealed class StubTransport : IPageLoadTransport
    {
        private readonly string _document;
        public int CallCount { get; private set; }
        public StubTransport(string document) => _document = document;
        public Task<string> LoadAsync(PageRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_document);
        }
    }

    private sealed class ThrowingTransport : IPageLoadTransport
    {
        public Task<string> LoadAsync(PageRequest request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The cache short-circuit must prevent this call.");
    }

    private sealed class ThrowingWriteCache : IPageCache
    {
        public Task<string?> TryReadAsync(string url, PageType pageType, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);
        public Task WriteAsync(string url, PageType pageType, string document, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Cache backend down.");
    }
}
