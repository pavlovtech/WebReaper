using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using WebReaper.Core.Observability.Abstract;

namespace WebReaper.Core.Observability.Concrete;

/// <summary>
/// ADR-0018. The JSONL file trace adapter — one event per line, appended
/// to a path supplied at construction. Mirrors the buffered-drain pattern
/// from <c>BufferedFileSink</c> (ADR-0006): the
/// <see cref="IExtractionTrace.RecordAsync"/> hot path enqueues to a
/// <see cref="BlockingCollection{T}"/>; a single background consumer
/// drains it to disk with one append per event. <c>RecordAsync</c>
/// completes synchronously (no I/O), keeping the Spider shell's path
/// allocation-free for the event itself.
/// </summary>
/// <remarks>
/// <para>
/// JSON shape per line: <c>{"ts":"...","kind":"PageLoadStarted","url":"...","pageType":"Static"}</c>.
/// <c>kind</c> is the arm's type name; the arm's payload fields are
/// flattened next to <c>ts</c> + <c>url</c>. <c>Result</c> on
/// <c>ExtractionCompleted</c> is nested as a JSON object literal.
/// </para>
/// <para>
/// Crash-safety: the consumer's loop is bound to the supplied
/// <see cref="CancellationToken"/>; a clean shutdown drains the queue.
/// A hard host kill loses whatever's still in the buffer — the same
/// trade-off as <c>BufferedFileSink</c>. For stronger durability the
/// hosted-replay satellite (deferred) ships a remote-ingest path.
/// </para>
/// </remarks>
public sealed class FileExtractionTrace : IExtractionTrace, IDisposable
{
    private readonly BlockingCollection<TraceEvent> _queue = new();
    private readonly string _filePath;
    private readonly object _initLock = new();
    private bool _consuming;
    private bool _disposed;

    /// <summary>Open the trace for appending to
    /// <paramref name="filePath"/>. The file is appended-to (not
    /// truncated) so multiple runs accumulate.</summary>
    /// <param name="filePath">Absolute or working-dir-relative path the
    /// JSONL is appended to. The containing directory must exist (no
    /// auto-create — kept explicit so a typo doesn't silently make a
    /// trace in the wrong place).</param>
    public FileExtractionTrace(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    /// <inheritdoc />
    public ValueTask RecordAsync(TraceEvent ev, CancellationToken cancellationToken = default)
    {
        if (_disposed) return ValueTask.CompletedTask;
        EnsureConsuming(cancellationToken);
        try { _queue.Add(ev, cancellationToken); }
        catch (InvalidOperationException) { /* queue closed mid-call */ }
        catch (OperationCanceledException) { /* caller cancelled; honour */ throw; }
        return ValueTask.CompletedTask;
    }

    /// <summary>Stop accepting events; let the background consumer drain
    /// whatever's queued and exit.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _queue.CompleteAdding();
    }

    private void EnsureConsuming(CancellationToken ct)
    {
        if (_consuming) return;
        lock (_initLock)
        {
            if (_consuming) return;
            _ = Task.Run(() => DrainAsync(ct), ct);
            _consuming = true;
        }
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        try
        {
            foreach (var ev in _queue.GetConsumingEnumerable(ct))
            {
                await File.AppendAllTextAsync(_filePath,
                    Serialize(ev) + Environment.NewLine, ct);
            }
        }
        catch (OperationCanceledException) { /* clean shutdown */ }
    }

    /// <summary>Serialize a <see cref="TraceEvent"/> to a one-line
    /// JSON object. Internal — exposed for unit-testing the line shape
    /// without going through the file path.</summary>
    internal static string Serialize(TraceEvent ev)
    {
        var obj = new JsonObject
        {
            ["ts"] = ev.Timestamp.ToString("O"),
            ["kind"] = ev.GetType().Name,
            ["url"] = ev.Url,
        };
        switch (ev)
        {
            case TraceEvent.PageLoadStarted s:
                obj["pageType"] = s.PageType.ToString();
                break;
            case TraceEvent.PageLoadCompleted c:
                obj["bytes"] = c.Bytes;
                break;
            case TraceEvent.PageLoadFailed f:
                obj["exceptionType"] = f.ExceptionType;
                obj["message"] = f.Message;
                break;
            case TraceEvent.ExtractionStarted s:
                obj["schemaHash"] = s.SchemaHash;
                break;
            case TraceEvent.ExtractionCompleted c:
                // Use the explicit JsonNode overload of indexer-set: the
                // generic Add<T>(T) is AOT-hostile (IL2026/IL3050) per
                // ADR-0052's deep-clone learnings.
                obj["result"] = (JsonNode?)c.Result.DeepClone();
                break;
            case TraceEvent.PageProcessed p:
                obj["verdict"] = p.Verdict;
                break;
            case TraceEvent.SinkEmit s:
                obj["sinkName"] = s.SinkName;
                break;
            case TraceEvent.CrawlStopped s:
                obj["reason"] = s.Reason;
                break;
        }
        return obj.ToJsonString();
    }
}
