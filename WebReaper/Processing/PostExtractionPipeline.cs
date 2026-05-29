using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Domain.Parsing;
using WebReaper.Infra.Abstract;
using WebReaper.Processing.Abstract;
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Models;

namespace WebReaper.Processing;

/// <summary>
/// The one runtime home for the post-extraction surface (ADR-0076): run a
/// <see cref="ParsedData"/> through the <see cref="IPageProcessor"/> pipeline
/// (Process), and if it survives, fan it out to every <see cref="IScraperSink"/>
/// (Emit). Both the in-process Crawl driver
/// (<see cref="WebReaper.Core.ScraperEngine"/>) and the Agent driver
/// (<see cref="WebReaper.Core.Agent.Concrete.AgentEngine"/>) delegate here, and
/// the consumer-authored distributed driver (ADR-0009) can reuse it rather than
/// re-deriving the deep-clone fan-out and the disposal order by hand.
/// <para>
/// It <b>holds</b> the processors and sinks and owns their lifecycle: it is an
/// <see cref="IAsyncInitializable"/> (ADR-0033) and <see cref="IAsyncDisposable"/>
/// (ADR-0058) participant, so a driver warms and disposes the whole
/// post-extraction slice as one adapter. The ADR-0058 reverse order holds by
/// composition — the driver disposes this module ahead of the
/// <c>IVisitedLinkTracker</c> / scheduler, and inside it processors are disposed
/// before sinks.
/// </para>
/// </summary>
public sealed class PostExtractionPipeline : IAsyncInitializable, IAsyncDisposable
{
    private readonly IReadOnlyList<IPageProcessor> _processors;
    private readonly IReadOnlyList<IScraperSink> _sinks;
    private readonly ILogger _logger;
    private bool _warmedUp;
    private bool _disposed;

    /// <summary>
    /// Construct over the sinks and (optional) page processors the driver holds.
    /// </summary>
    /// <param name="sinks">The Sink fan-out targets, in registration order.</param>
    /// <param name="processors">The page-processor pipeline, in registration
    /// order; processor N sees processor N-1's record. Null or empty means no
    /// processing — the record passes straight to the sinks.</param>
    /// <param name="logger">Diagnostics sink; defaults to
    /// <see cref="NullLogger.Instance"/>.</param>
    public PostExtractionPipeline(
        IReadOnlyList<IScraperSink> sinks,
        IReadOnlyList<IPageProcessor>? processors = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(sinks);
        _sinks = sinks;
        _processors = processors ?? Array.Empty<IPageProcessor>();
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// ADR-0033 warm-up: initialise every held sink and processor that declares
    /// the <see cref="IAsyncInitializable"/> capability — once, before first use.
    /// Idempotent: a second call is a no-op. Adapters with no async warm-up (the
    /// console / file sinks) are skipped.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_warmedUp) return;
        _warmedUp = true;

        foreach (var sink in _sinks)
            if (sink is IAsyncInitializable initializableSink)
                await initializableSink.InitializeAsync();

        // ADR-0038: a page processor holding a durable resource (an LLM client)
        // opts into the same warm-up capability.
        foreach (var processor in _processors)
            if (processor is IAsyncInitializable initializableProcessor)
                await initializableProcessor.InitializeAsync();
    }

    /// <summary>
    /// Run <paramref name="record"/> through the page-processor pipeline, then,
    /// if it survived, fan the result out to every sink and return it; if a
    /// processor dropped the page (a <see cref="PageVerdict.Dropped"/> verdict,
    /// or a processor that threw anything but
    /// <see cref="OperationCanceledException"/>), emit to no sink and return
    /// <c>null</c>. The drop-means-no-emit invariant lives here, in one place.
    /// <para>
    /// Each sink receives its own deep-clone of <see cref="ParsedData.Data"/>
    /// (ADR-0031) — the fan-out runs the sinks concurrently and
    /// <see cref="JsonObject"/> is not thread-safe, so a shared instance would
    /// race. A processor that throws drops the page and the run continues — a
    /// noisy page never aborts the crawl (ADR-0029).
    /// </para>
    /// </summary>
    /// <param name="record">The extracted record to process and emit, with the
    /// page URL already folded into <c>Data["url"]</c> (ADR-0031).</param>
    /// <param name="html">The raw page body the record was extracted from —
    /// the input a re-extraction or confidence-scoring processor reads.</param>
    /// <param name="backLinks">Ancestor URLs that led to this page, oldest
    /// first (empty for the Agent driver, which is page-spanning not
    /// link-following).</param>
    /// <param name="schema">The extraction Schema the fold ran, or null.</param>
    /// <param name="cancellationToken">Cancels the pipeline and the fan-out.</param>
    /// <returns>The surviving record (the value the last processor kept, or
    /// <paramref name="record"/> when no processor ran), or <c>null</c> when the
    /// page was dropped.</returns>
    public async Task<ParsedData?> ProcessAndEmitAsync(
        ParsedData record,
        string html,
        IReadOnlyList<string> backLinks,
        Schema? schema,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        // ADR-0038: the page-processor pipeline runs over the extracted record
        // BEFORE the Sink fan-out — enrich / observe / filter / repair, in
        // registration order, each processor handed the previous one's record.
        var current = record;
        foreach (var processor in _processors)
        {
            var context = new PageContext(current, html, backLinks, schema);

            PageVerdict verdict;
            try
            {
                verdict = await processor.ProcessAsync(context, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Page processor {Processor} threw on {Url}; dropping the page",
                    processor.GetType().Name, current.Url);
                return null;
            }

            switch (verdict)
            {
                case PageVerdict.Dropped dropped:
                    _logger.LogInformation("Page {Url} dropped by {Processor}: {Reason}",
                        current.Url, processor.GetType().Name, dropped.Reason);
                    return null;
                case PageVerdict.Kept kept:
                    current = kept.Data;
                    break;
            }
        }

        if (_sinks.Count > 0)
        {
            // ADR-0031: hand each sink its own deep-cloned Data. The `with` copy
            // bypasses ParsedData's merge initializer (the clone is of the
            // already-merged Data), so there is no double-merge.
            var sinkTasks = _sinks.Select(sink => sink.EmitAsync(
                current with { Data = (JsonObject)current.Data.DeepClone() }, cancellationToken));
            await Task.WhenAll(sinkTasks);
        }

        return current;
    }

    /// <summary>
    /// ADR-0058 teardown: dispose held processors then sinks, each in reverse
    /// registration order, so a dependent adapter sees its dependency still
    /// valid mid-flush. Per-adapter disposal exceptions log at Warning and are
    /// swallowed — a successful run is not retroactively failed by a teardown
    /// burp. Idempotent.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        for (var i = _processors.Count - 1; i >= 0; i--)
            await SafeDisposeAsync(_processors[i]);
        for (var i = _sinks.Count - 1; i >= 0; i--)
            await SafeDisposeAsync(_sinks[i]);
    }

    private async ValueTask SafeDisposeAsync(object? obj)
    {
        try
        {
            switch (obj)
            {
                case IAsyncDisposable a: await a.DisposeAsync(); break;
                case IDisposable d: d.Dispose(); break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Disposal of {Type} threw", obj?.GetType().Name ?? "(null)");
        }
    }
}
