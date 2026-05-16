using System.Collections.Concurrent;

namespace WebReaper.DataAccess;

/// <summary>
/// In-process <see cref="IKeyedBlobStore"/>. Holds the serialized blob, not
/// the live object — so the in-memory path exercises the same payload-shell
/// serialization the persistent backends do (ADR 0003).
/// </summary>
public class InMemoryBlobStore : IKeyedBlobStore
{
    private readonly ConcurrentDictionary<string, string> _blobs = new();

    public Task PutAsync(string key, string value)
    {
        _blobs[key] = value;
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string key)
        => Task.FromResult(_blobs.TryGetValue(key, out var value) ? value : null);
}
