using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Concrete;

/// <summary>
/// <see cref="IContentExtractor"/> wrapper that infers a schema on the first
/// call and reuses it for the rest of the crawl (ADR-0067). The
/// LLM-as-proposer / deterministic-as-validator wedge applied to schema
/// generation — the fifth dock of the pattern (sibling to ADR-0046 routing,
/// ADR-0047 selector repair, ADR-0050 action resolution, ADR-0051 page
/// selection):
/// <list type="bullet">
/// <item>First page: call <see cref="ISchemaInferrer.InferAsync"/>; cache the
/// result on this instance.</item>
/// <item>Subsequent pages: delegate to the inner
/// <see cref="IContentExtractor"/> (typically <see cref="SchemaFold{TNode}"/>)
/// with the cached schema — pure deterministic path, no LLM cost.</item>
/// </list>
/// <para>
/// Cache lifecycle is per-instance: a fresh engine = fresh inference; consecutive
/// <c>RunAsync</c> calls on the same engine reuse. <see cref="SemaphoreSlim"/>
/// guards the double-checked-locking inference call so parallel first-page
/// workers don't race (the <c>Parallel.ForEachAsync</c> driver may seed
/// multiple workers at once).
/// </para>
/// <para>
/// The <see cref="IContentExtractor.ExtractAsync"/> schema argument is ignored
/// — the consumer chose <c>.ExtractInferred(...)</c> precisely because they
/// did not supply one. The inner extractor sees the inferred schema instead.
/// </para>
/// </summary>
public sealed class LearnedSchemaContentExtractor : IContentExtractor, IAsyncDisposable
{
    private readonly ISchemaInferrer _inferrer;
    private readonly IContentExtractor _inner;
    private readonly string? _goal;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lock = new(initialCount: 1, maxCount: 1);
    private Schema? _learned;
    private int _disposed;

    /// <summary>Compose an inferrer with an inner extractor.</summary>
    /// <param name="inferrer">The schema inferrer (required). The wrapper
    /// invokes <see cref="ISchemaInferrer.InferAsync"/> exactly once across the
    /// instance's lifetime — first <see cref="ExtractAsync"/> call wins; every
    /// subsequent call reuses the cached schema.</param>
    /// <param name="inner">The inner extractor that consumes the inferred
    /// schema. Typically the default <see cref="SchemaFold{TNode}"/>
    /// (ADR-0002 / ADR-0039); any <see cref="IContentExtractor"/> that
    /// accepts a non-null <see cref="Schema"/> works.</param>
    /// <param name="goal">Optional natural-language hint passed to the
    /// inferrer (<c>"product details"</c>, <c>"job listings"</c>, …).</param>
    /// <param name="logger">Optional logger; <see cref="NullLogger.Instance"/>
    /// when omitted.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="inferrer"/> or <paramref name="inner"/> is null.</exception>
    public LearnedSchemaContentExtractor(
        ISchemaInferrer inferrer,
        IContentExtractor inner,
        string? goal = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(inferrer);
        ArgumentNullException.ThrowIfNull(inner);
        _inferrer = inferrer;
        _inner = inner;
        _goal = goal;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc/>
    public async Task<JsonObject> ExtractAsync(string document, Schema? schema)
    {
        // ADR-0067: the schema argument is ignored — the consumer chose
        // ExtractInferred precisely because they did NOT supply one. The
        // inner extractor sees the inferred schema instead.
        _ = schema;

        var learned = Volatile.Read(ref _learned);
        if (learned is null)
        {
            // Double-checked locking around the first-page inference. The
            // Parallel.ForEachAsync driver may seed multiple workers at
            // once; without the semaphore each would pay the LLM.
            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                learned = _learned;
                if (learned is null)
                {
                    _logger.LogInformation(
                        "LearnedSchemaContentExtractor: inferring schema (goal='{Goal}')",
                        _goal ?? "(none)");
                    learned = await _inferrer.InferAsync(document, _goal).ConfigureAwait(false);
                    if (learned is null)
                    {
                        throw new InvalidOperationException(
                            "ISchemaInferrer.InferAsync returned null. Inferrers must " +
                            "return a non-null Schema or throw.");
                    }
                    Volatile.Write(ref _learned, learned);
                    _logger.LogInformation(
                        "LearnedSchemaContentExtractor: inferred schema with {FieldCount} field(s)",
                        learned.Children.Count);
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        return await _inner.ExtractAsync(document, learned).ConfigureAwait(false);
    }

    /// <summary>
    /// The schema produced by the first <see cref="ExtractAsync"/> call.
    /// Returns <c>null</c> before that call completes; non-null thereafter.
    /// Surfaced for diagnostics and the v2 source-gen emit path (ADR-0067
    /// fork 10 deferral — log + property is the v1 path; consumer pastes
    /// into <c>[ScrapeSchema]</c> when ready to lock).
    /// </summary>
    public Schema? InferredSchema => Volatile.Read(ref _learned);

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        // Guard against double-dispose — the engine teardown (ADR-0058)
        // walks builder hooks once but consumers may dispose this directly
        // too; a SemaphoreSlim.Dispose on an already-disposed instance
        // throws ObjectDisposedException, which the engine would log at
        // Warning. Quiet-idempotent is the right shape for a wrapper.
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _lock.Dispose();
        }
        return ValueTask.CompletedTask;
    }
}
