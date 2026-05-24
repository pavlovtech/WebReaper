using System.Collections.Concurrent;
using WebReaper.Core.Loaders.Abstract;
using WebReaper.Domain.Selectors;

namespace WebReaper.Core.Loaders.Concrete;

/// <summary>
/// The firecrawl-shaped TTL <see cref="IPageCache"/> adapter (ADR-0041):
/// in-memory, thread-safe, lazy-evicted. An entry older than
/// <see cref="MaxAge"/> is reported as a miss; eviction is opportunistic
/// (the next write to the same key overwrites). Reached via
/// <c>ScraperEngineBuilder.WithMaxAge(maxAge)</c>.
/// <para>
/// <see cref="MaxAge"/> = <see cref="TimeSpan.Zero"/> is the
/// "store but never serve" mode: every <see cref="TryReadAsync"/> reports a
/// miss, every <see cref="WriteAsync"/> stores. Useful for change-tracking
/// (ADR-0048) and ergonomic "force-fresh" crawls.
/// </para>
/// <para>
/// Not shared across processes by design — a distributed crawl wires a
/// satellite (e.g. a future <c>RedisPageCache</c>) the same way it wires the
/// other distributed adapters. The visited-link tracker (ADR-0022) bounds
/// the cache's URL set to first-time-fetched pages, so unbounded memory is
/// structurally prevented for any single Crawl.
/// </para>
/// </summary>
public sealed class InMemoryPageCache : IPageCache
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly TimeSpan _maxAge;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>The TTL configured at construction.</summary>
    public TimeSpan MaxAge => _maxAge;

    /// <summary>Construct with a TTL.</summary>
    /// <param name="maxAge">Entries older than this are reported as a
    /// miss. <see cref="TimeSpan.Zero"/> stores but never serves.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxAge"/> is negative.</exception>
    public InMemoryPageCache(TimeSpan maxAge)
        : this(maxAge, () => DateTimeOffset.UtcNow) { }

    // Test seam: a deterministic clock makes the TTL boundary observable
    // without Thread.Sleep. Kept internal — no public clock-injection API
    // for the standard PageLoader integration.
    internal InMemoryPageCache(TimeSpan maxAge, Func<DateTimeOffset> clock)
    {
        if (maxAge < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxAge), maxAge,
                "maxAge must be non-negative; use TimeSpan.Zero to store but never serve.");
        _maxAge = maxAge;
        _clock = clock;
    }

    /// <inheritdoc/>
    public Task<string?> TryReadAsync(string url, PageType pageType, CancellationToken cancellationToken)
    {
        if (_maxAge == TimeSpan.Zero) return Task.FromResult<string?>(null);

        if (!_entries.TryGetValue(Key(url, pageType), out var entry))
            return Task.FromResult<string?>(null);

        if (_clock() - entry.StoredAt > _maxAge)
            return Task.FromResult<string?>(null);

        return Task.FromResult<string?>(entry.Document);
    }

    /// <inheritdoc/>
    public Task WriteAsync(string url, PageType pageType, string document, CancellationToken cancellationToken)
    {
        _entries[Key(url, pageType)] = new Entry(document, _clock());
        return Task.CompletedTask;
    }

    /// <summary>Drop every cached entry. Test affordance.</summary>
    public void Clear() => _entries.Clear();

    // The cache key. PageType is part of the key because a Static and a
    // Dynamic load of the same URL can return different HTML.
    private static string Key(string url, PageType pageType) => $"{pageType}:{url}";

    private readonly record struct Entry(string Document, DateTimeOffset StoredAt);
}
