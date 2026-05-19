using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using WebReaper.DataAccess;
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Models;

namespace WebReaper.Sinks.Concrete;

/// <summary>
/// The one home for the file-sink buffered drain (ADR 0006). A producer
/// (<see cref="EmitAsync"/>) hands rows to a <see cref="BlockingCollection{T}"/>;
/// a single background consumer appends them to the file. Replaces the
/// buffered-drain code that was copy-pasted across the two file sinks and had
/// drifted into divergent bugs.
///
/// Cleanup and directory creation are delegated to FilePersistencePrep
/// (ADR-0011) and happen once, eagerly, in the constructor —
/// deterministic and correct even for a crawl that produces zero rows (the
/// behaviour the old JSON-lines sink had and the old CSV sink lacked), and the
/// directory is created regardless of <c>dataCleanupOnStart</c> (the old
/// JSON-lines sink only created it when cleaning). The single consumer is
/// started once, thread-safely, by whichever <see cref="EmitAsync"/> runs
/// first and is bound to that call's token (the old JSON-lines sink bound it
/// to <c>CancellationToken.None</c> with dead re-init code; the old CSV sink's
/// async init guard could double-spawn it).
///
/// Per-format rendering is the only legitimate variation and lives behind
/// <see cref="IFileSinkFormat"/>; this class never knows JSON from CSV.
/// Unchanged from the originals (a shared property, not this candidate's
/// concern — a future candidate): one <see cref="File.AppendAllTextAsync(string,string,CancellationToken)"/>
/// per row opens and closes the file each time, and the consumer has no
/// flush/dispose — its lifetime is the process or the bound token.
/// </summary>
internal class BufferedFileSink : IScraperSink
{
    private readonly BlockingCollection<JsonObject> _entries = new();
    private readonly IFileSinkFormat _format;
    private readonly string _filePath;
    private readonly object _initLock = new();
    private bool _consuming;

    public BufferedFileSink(string filePath, bool dataCleanupOnStart, IFileSinkFormat format)
    {
        _filePath = filePath;
        _format = format;
        DataCleanupOnStart = dataCleanupOnStart;

        // ADR-0011: directory creation + cleanup-on-start delegated to
        // FilePersistencePrep. The buffered drain, IFileSinkFormat and the
        // per-row append are this adapter's own essence, unchanged —
        // ADR-0006's open/close-per-row fence stands (not closed here).
        FilePersistencePrep.EnsureDirectory(filePath);
        FilePersistencePrep.CleanupOnStart(filePath, dataCleanupOnStart);
    }

    public bool DataCleanupOnStart { get; set; }

    public Task EmitAsync(ParsedData entity, CancellationToken cancellationToken = default)
    {
        entity.Data["url"] = entity.Url;

        EnsureConsuming(cancellationToken);

        _entries.Add(entity.Data, cancellationToken);

        return Task.CompletedTask;
    }

    private void EnsureConsuming(CancellationToken cancellationToken)
    {
        if (_consuming) return;

        lock (_initLock)
        {
            if (_consuming) return;

            _ = Task.Run(() => DrainAsync(cancellationToken), cancellationToken);
            _consuming = true;
        }
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        var first = true;

        foreach (var entry in _entries.GetConsumingEnumerable(cancellationToken))
        {
            if (first)
            {
                first = false;

                var header = _format.Header(entry);
                if (header is not null)
                    await File.AppendAllTextAsync(
                        _filePath, header + Environment.NewLine, cancellationToken);
            }

            await File.AppendAllTextAsync(
                _filePath, _format.FormatRow(entry) + Environment.NewLine, cancellationToken);
        }
    }
}
