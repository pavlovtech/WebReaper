using System.Collections.Concurrent;
using WebReaper.Processing.Abstract;

namespace WebReaper.Processing.Concrete;

/// <summary>
/// The default <see cref="IChangeStore"/> (ADR-0048): a thread-safe
/// in-memory dictionary keyed by URL. Per-process; not shared across
/// Crawls unless the consumer wires a satellite adapter.
/// </summary>
public sealed class InMemoryChangeStore : IChangeStore
{
    private readonly ConcurrentDictionary<string, string> _hashes = new();

    /// <inheritdoc/>
    public Task<string?> TryReadAsync(string url, CancellationToken cancellationToken)
        => Task.FromResult(_hashes.TryGetValue(url, out var hash) ? hash : null);

    /// <inheritdoc/>
    public Task WriteAsync(string url, string hash, CancellationToken cancellationToken)
    {
        _hashes[url] = hash;
        return Task.CompletedTask;
    }

    /// <summary>Drop every stored hash. Test affordance.</summary>
    public void Clear() => _hashes.Clear();
}
