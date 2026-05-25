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
/// <para>
/// ADR-0069: an optional <see cref="ISchemaValidator"/> + N-failure threshold
/// gate the cache. When the validator rejects N consecutive inner-extractor
/// outputs, the cache is dropped so the next call re-infers from a fresh
/// page — same proposer-validator shape applied to schema lifecycle. The
/// <c>reInferAfterFailures = 0</c> default preserves ADR-0067 v1
/// trust-the-cache behaviour; the satellite <c>LlmSchemaInferrer</c>
/// flips that default to 3.
/// </para>
/// </summary>
public sealed class LearnedSchemaContentExtractor : IContentExtractor, IAsyncDisposable
{
    private readonly ISchemaInferrer _inferrer;
    private readonly IContentExtractor _inner;
    private readonly string? _goal;
    private readonly ILogger _logger;
    private readonly ISchemaValidator _validator;
    private readonly int _reInferAfterFailures;
    private readonly int _maxReInferences;
    private readonly SemaphoreSlim _lock = new(initialCount: 1, maxCount: 1);
    private Schema? _learned;
    private int _disposed;
    private int _consecutiveFailures;
    private int _reInferencesUsed;

    /// <summary>Compose an inferrer with an inner extractor.</summary>
    /// <param name="inferrer">The schema inferrer (required). The wrapper
    /// invokes <see cref="ISchemaInferrer.InferAsync"/> on the first
    /// <see cref="ExtractAsync"/> call; the result caches for the
    /// instance's lifetime — or until validator-driven re-inference
    /// (ADR-0069) drops it.</param>
    /// <param name="inner">The inner extractor that consumes the inferred
    /// schema. Typically the default <see cref="SchemaFold{TNode}"/>
    /// (ADR-0002 / ADR-0039); any <see cref="IContentExtractor"/> that
    /// accepts a non-null <see cref="Schema"/> works.</param>
    /// <param name="goal">Optional natural-language hint passed to the
    /// inferrer (<c>"product details"</c>, <c>"job listings"</c>, …).</param>
    /// <param name="logger">Optional logger; <see cref="NullLogger.Instance"/>
    /// when omitted.</param>
    /// <param name="validator">Optional <see cref="ISchemaValidator"/>
    /// (ADR-0062 + ADR-0069). Defaults to
    /// <see cref="SchemaSatisfiedValidator.Instance"/>. Consulted on every
    /// extraction result; with <paramref name="reInferAfterFailures"/>
    /// &gt; 0, N consecutive invalid verdicts drop the cached schema and
    /// trigger re-inference on the next call.</param>
    /// <param name="reInferAfterFailures">Number of consecutive validator
    /// failures before dropping the cached schema (ADR-0069). Default
    /// <c>0</c> = never re-infer (preserves ADR-0067 v1 trust-the-cache
    /// behaviour). The satellite <see cref="LlmSchemaInferrer"/> ships
    /// <c>3</c> as the per-role default via
    /// <c>LlmSchemaInferrerOptions.ReInferAfterFailures</c>.</param>
    /// <param name="maxReInferencesPerInstance">Cost cap — once the
    /// wrapper has dropped + re-inferred this many times, further
    /// failures keep the stale schema and log at Warning. Default
    /// <c>int.MaxValue</c> (unbounded; cap is the consumer's
    /// guardrail).</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="inferrer"/> or <paramref name="inner"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="reInferAfterFailures"/> or
    /// <paramref name="maxReInferencesPerInstance"/> is negative.</exception>
    public LearnedSchemaContentExtractor(
        ISchemaInferrer inferrer,
        IContentExtractor inner,
        string? goal = null,
        ILogger? logger = null,
        ISchemaValidator? validator = null,
        int reInferAfterFailures = 0,
        int maxReInferencesPerInstance = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(inferrer);
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegative(reInferAfterFailures);
        ArgumentOutOfRangeException.ThrowIfNegative(maxReInferencesPerInstance);
        _inferrer = inferrer;
        _inner = inner;
        _goal = goal;
        _logger = logger ?? NullLogger.Instance;
        _validator = validator ?? SchemaSatisfiedValidator.Instance;
        _reInferAfterFailures = reInferAfterFailures;
        _maxReInferences = maxReInferencesPerInstance;
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

        var result = await _inner.ExtractAsync(document, learned).ConfigureAwait(false);

        // ADR-0069: validate the inner extractor's output; on consecutive
        // failure, optionally drop the cache to trigger re-inference on
        // the next call. The cost-cap branch keeps the run going with the
        // stale schema once the cap is hit.
        if (_reInferAfterFailures > 0)
        {
            var verdict = _validator.Validate(result, learned);
            if (verdict.IsValid)
            {
                // Reset on any success — only *consecutive* failures
                // trigger re-inference. Outlier pages (a single empty
                // listing) don't burn an LLM call.
                Volatile.Write(ref _consecutiveFailures, 0);
            }
            else
            {
                var failures = Interlocked.Increment(ref _consecutiveFailures);
                if (failures >= _reInferAfterFailures)
                {
                    await TryDropCacheForReInferenceAsync(learned, verdict.Reason)
                        .ConfigureAwait(false);
                }
            }
        }

        return result;
    }

    // ADR-0069: the cache-clear path. Acquires the same SemaphoreSlim the
    // first-page inference uses so a concurrent worker's re-inference is
    // serialised. Reference-identity check against `staleSchema` ensures
    // we don't double-clear when multiple workers observe the same failure
    // before the lock is acquired.
    private async Task TryDropCacheForReInferenceAsync(Schema staleSchema, string? reason)
    {
        // Pre-flight cost cap. Increment optimistically, undo on cap-hit
        // — keeps the no-cap-hit path lock-free.
        if (Interlocked.Increment(ref _reInferencesUsed) > _maxReInferences)
        {
            Interlocked.Decrement(ref _reInferencesUsed);
            _logger.LogWarning(
                "LearnedSchemaContentExtractor: hit MaxReInferencesPerInstance " +
                "({Cap}); keeping the stale schema (validator reason: '{Reason}'). " +
                "Records will continue to fail validation; consider raising " +
                "MaxReInferencesPerInstance or investigating the underlying cause.",
                _maxReInferences, reason ?? "(none)");
            return;
        }

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Only clear if the cached schema is still the one we observed
            // failing — another worker may have re-inferred between our
            // failure observation and acquiring the lock. Reference identity
            // is the safe check (the wrapper only ever swaps the field via
            // Volatile.Write; the inferrer must return a fresh Schema
            // instance to be observably different).
            if (ReferenceEquals(_learned, staleSchema))
            {
                _logger.LogInformation(
                    "LearnedSchemaContentExtractor: dropping cached schema after " +
                    "{Failures} consecutive validation failures (reason: '{Reason}'); " +
                    "next call will re-infer (re-inferences used: {Used}/{Cap}).",
                    _consecutiveFailures, reason ?? "(none)",
                    _reInferencesUsed, _maxReInferences);
                Volatile.Write(ref _learned, null);
                Volatile.Write(ref _consecutiveFailures, 0);
            }
            else
            {
                // Lost the race — someone else already re-inferred. Undo
                // our over-incremented re-inference counter (we never
                // actually used one).
                Interlocked.Decrement(ref _reInferencesUsed);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// The schema produced by the most recent <see cref="ExtractAsync"/>
    /// inference call. Returns <c>null</c> before the first call completes;
    /// non-null thereafter. After validator-driven re-inference (ADR-0069)
    /// returns the newly-inferred schema, not the dropped one.
    /// <para>
    /// Surfaced for diagnostics and the v2 source-gen emit path (ADR-0067
    /// fork 10 deferral — log + property is the v1 path; consumer pastes
    /// into <c>[ScrapeSchema]</c> when ready to lock).
    /// </para>
    /// </summary>
    public Schema? InferredSchema => Volatile.Read(ref _learned);

    /// <summary>
    /// The number of times the wrapper has dropped the cached schema and
    /// re-inferred (ADR-0069). 0 if validator-driven re-inference is
    /// disabled (<c>reInferAfterFailures = 0</c>) or no failures have
    /// crossed the threshold. Capped at the
    /// <c>maxReInferencesPerInstance</c> ctor argument.
    /// </summary>
    public int ReInferencesUsed => Volatile.Read(ref _reInferencesUsed);

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
