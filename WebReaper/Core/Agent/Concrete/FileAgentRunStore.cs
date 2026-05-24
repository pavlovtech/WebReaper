using WebReaper.Core.Agent.Abstract;
using WebReaper.DataAccess;
using WebReaper.Domain.Agent;
using WebReaper.Serialization;

namespace WebReaper.Core.Agent.Concrete;

/// <summary>
/// The File adapter of the <see cref="IAgentRunStore"/> seam (ADR-0051
/// §Decision §6). One JSON file per <c>runId</c> under
/// <see cref="_directory"/>; the file is the entire snapshot, rewritten on
/// every <see cref="SaveStepAsync"/>. Satisfies ADR-0036's ≥2-adapter rule
/// alongside the in-memory default.
/// <para>
/// Suitable for single-process resumable agents — the firecrawl-shaped CLI,
/// a single-machine batch job, a desktop demo. Distributed callers and
/// callers with the Redis / Mongo / Sqlite / Cosmos satellites already in
/// their stack swap in one of those adapters (ADR-0009 pattern).
/// </para>
/// </summary>
internal sealed class FileAgentRunStore : IAgentRunStore
{
    private readonly string _directory;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public FileAgentRunStore(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        _directory = directory;
        FilePersistencePrep.EnsureDirectory(Path.Combine(directory, "_"));
    }

    /// <inheritdoc/>
    public async ValueTask<AgentRunSnapshot?> LoadAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(runId);
        var path = PathFor(runId);
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path)) return null;
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            return WebReaperAgentJson.DeserializeSnapshot(json);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask SaveStepAsync(
        string runId,
        AgentDecision decision,
        AgentRunSnapshot postState,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(runId);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(postState);
        var path = PathFor(runId);
        var json = WebReaperAgentJson.SerializeSnapshot(postState);
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await File.WriteAllTextAsync(path, json, cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DeleteAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(runId);
        var path = PathFor(runId);
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    // Sanitize runId for use as a filename — runIds are caller-supplied so
    // can contain any string. Use a percent-encoding-style escape so the
    // mapping is injective and reversible (though we never read it back).
    private string PathFor(string runId)
    {
        var safe = string.Concat(runId.Select(c =>
            char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c.ToString() : $"%{(int)c:X2}"));
        return Path.Combine(_directory, $"{safe}.json");
    }
}
