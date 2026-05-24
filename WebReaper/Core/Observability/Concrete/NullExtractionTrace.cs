using WebReaper.Core.Observability.Abstract;

namespace WebReaper.Core.Observability.Concrete;

/// <summary>
/// ADR-0018. The default <see cref="IExtractionTrace"/> — drops every
/// event. One virtual call per event; no allocation (returns
/// <see cref="ValueTask.CompletedTask"/>). The hot path is free of
/// allocation when no operator has wired a real trace adapter.
/// </summary>
public sealed class NullExtractionTrace : IExtractionTrace
{
    /// <summary>The singleton instance — registered by default in
    /// <see cref="WebReaper.Builders.ScraperEngineBuilder"/> unless a
    /// consumer calls <c>WithExtractionTrace</c> or <c>TraceToFile</c>.</summary>
    public static readonly NullExtractionTrace Instance = new();

    private NullExtractionTrace() { }

    /// <inheritdoc />
    public ValueTask RecordAsync(TraceEvent ev, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}
