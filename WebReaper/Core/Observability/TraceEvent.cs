using System.Text.Json.Nodes;
using WebReaper.Domain.Selectors;

namespace WebReaper.Core.Observability;

/// <summary>
/// ADR-0018 closed-sum page-lifecycle trace event. One arm per moment of
/// interest in the load → extract → process → emit pipeline. Adapters
/// (<see cref="WebReaper.Core.Observability.Abstract.IExtractionTrace"/>)
/// dispatch on the arm; new arms are additive and trigger exhaustiveness
/// pressure in switch expressions.
/// </summary>
/// <remarks>
/// <para>
/// Url + Timestamp on the base; each arm carries its own payload.
/// The classic closed-sum shape (same pattern as
/// <c>PageAction</c>, ADR-0035, and <c>CrawlOutcome</c>, ADR-0001):
/// a <c>private</c> parameterless base constructor + nested
/// <c>sealed record</c> arms.
/// </para>
/// <para>
/// v1 ships a slightly leaner field set than ADR-0018 §"Decision §1"
/// originally sketched: <see cref="PageLoadCompleted"/> drops
/// <c>ContentType</c> (the <c>IPageLoadTransport</c> seam returns
/// <c>Task&lt;string&gt;</c> today; widening it to also surface the
/// response Content-Type is a deferred enrichment named in ADR-0056's
/// "HTTP-status surface" follow-up), and <see cref="ExtractionCompleted"/>
/// drops <c>MissingRequired</c> (the <c>SchemaSatisfiedValidator</c> from
/// ADR-0046 owns that signal — wiring it into the trace is a future
/// enrichment, not v1). Both are additive when their upstream sources
/// land.
/// </para>
/// </remarks>
public abstract record TraceEvent
{
    // Closed sum: only the nested sealed records below can extend this base.
    private TraceEvent() { }

    /// <summary>The URL of the page this event is about.</summary>
    public required string Url { get; init; }

    /// <summary>When the event was constructed (UTC).</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>The loader was asked to fetch the page.</summary>
    public sealed record PageLoadStarted(PageType PageType) : TraceEvent;

    /// <summary>The loader returned the page body.
    /// <paramref name="Bytes"/> is the UTF-8 length of the rendered document.</summary>
    public sealed record PageLoadCompleted(int Bytes) : TraceEvent;

    /// <summary>The loader threw before returning the page body.</summary>
    /// <param name="ExceptionType">The exception's runtime type name.</param>
    /// <param name="Message">The exception's <see cref="Exception.Message"/>.</param>
    public sealed record PageLoadFailed(string ExceptionType, string Message) : TraceEvent;

    /// <summary>The content extractor was invoked on the target page.</summary>
    /// <param name="SchemaHash">A stable hash of the <c>Schema</c> being
    /// extracted, or <c>null</c> for no-schema extractors
    /// (e.g. <c>MarkdownContentExtractor</c>, ADR-0040).</param>
    public sealed record ExtractionStarted(string? SchemaHash) : TraceEvent;

    /// <summary>The content extractor returned. The <paramref name="Result"/>
    /// is the typed <see cref="JsonObject"/> the extractor produced — the
    /// same shape the sinks ultimately emit.</summary>
    public sealed record ExtractionCompleted(JsonObject Result) : TraceEvent;

    /// <summary>The page-processor pipeline (ADR-0038) yielded a verdict on
    /// the extracted record.</summary>
    /// <param name="Verdict">A one-line description of the verdict — for
    /// <c>PageVerdict.Kept</c> the literal <c>"Kept"</c>; for
    /// <c>PageVerdict.Dropped</c>, <c>"Dropped: &lt;reason&gt;"</c>.</param>
    public sealed record PageProcessed(string Verdict) : TraceEvent;

    /// <summary>A sink was invoked with the extracted record. Fires once
    /// per sink per page (fan-out is sink-parallel; trace events are
    /// emitted before the sink-task is started, so order across sinks is
    /// not guaranteed).</summary>
    public sealed record SinkEmit(string SinkName) : TraceEvent;

    /// <summary>The stop rule concluded the Crawl. Fires once per run on
    /// natural completion; not fired on a caller-cancelled run.</summary>
    public sealed record CrawlStopped(string Reason) : TraceEvent;
}
