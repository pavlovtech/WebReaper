using System.Collections.Concurrent;

namespace WebReaper.AI.Llm;

/// <summary>
/// Default thread-safe <see cref="ILlmCallTelemetry"/> accumulator (ADR-0066).
/// Aggregate counters use <see cref="Interlocked"/>; the per-adapter
/// dictionary uses <see cref="ConcurrentDictionary{TKey,TValue}"/> with
/// mutable per-adapter stats whose fields are also guarded by
/// <see cref="Interlocked"/>.
/// <para>
/// <see cref="Snapshot"/> reads each counter atomically, but the cross-field
/// read is NOT transactional (a snapshot may see a half-updated aggregate
/// during heavy parallel traffic). This is acceptable — snapshots are
/// statistical, not balance-sheet. <see cref="Reset"/> uses
/// <see cref="Interlocked.Exchange(ref long, long)"/> to zero each counter
/// and clears the per-adapter dictionary in one call.
/// </para>
/// <para>
/// Null-token semantics: every aggregate field has a "has-value" sentinel
/// — when at least one recorded call surfaced a value, the snapshot field
/// reports the sum; when no call ever surfaced one, the snapshot field is
/// <c>null</c>. Distinguishes "all calls had null" (snapshot null) from
/// "some calls reported 0" (snapshot 0).
/// </para>
/// </summary>
public sealed class LlmCallTelemetry : ILlmCallTelemetry
{
    private long _callCount;
    private long _inputTokens;
    private long _outputTokens;
    private long _cachedInputTokens;
    private long _totalTokens;
    private long _parseRetries;
    private long _totalDurationTicks;
    // Has-value sentinels — see the type-level XML doc.
    private int _hasInput;
    private int _hasOutput;
    private int _hasCachedInput;
    private int _hasTotal;

    private readonly ConcurrentDictionary<string, AdapterStats> _byAdapter = new();

    /// <inheritdoc/>
    public void Record(LlmCallUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        Interlocked.Increment(ref _callCount);
        Interlocked.Add(ref _parseRetries, usage.ParseRetries);
        Interlocked.Add(ref _totalDurationTicks, usage.Duration.Ticks);
        if (usage.InputTokens is long i)
        {
            Interlocked.Add(ref _inputTokens, i);
            Interlocked.Exchange(ref _hasInput, 1);
        }
        if (usage.OutputTokens is long o)
        {
            Interlocked.Add(ref _outputTokens, o);
            Interlocked.Exchange(ref _hasOutput, 1);
        }
        if (usage.CachedInputTokens is long c)
        {
            Interlocked.Add(ref _cachedInputTokens, c);
            Interlocked.Exchange(ref _hasCachedInput, 1);
        }
        if (usage.TotalTokens is long t)
        {
            Interlocked.Add(ref _totalTokens, t);
            Interlocked.Exchange(ref _hasTotal, 1);
        }

        var perAdapter = _byAdapter.GetOrAdd(usage.DescriptorName, n => new AdapterStats(n));
        perAdapter.Add(usage);
    }

    /// <inheritdoc/>
    public LlmTelemetrySnapshot Snapshot()
    {
        // Build the per-adapter map first — order is independent.
        var perAdapter = _byAdapter.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.ToReadOnlySnapshot());

        return new LlmTelemetrySnapshot(
            CallCount: Interlocked.Read(ref _callCount),
            InputTokens: Volatile.Read(ref _hasInput) == 1
                ? Interlocked.Read(ref _inputTokens)
                : null,
            OutputTokens: Volatile.Read(ref _hasOutput) == 1
                ? Interlocked.Read(ref _outputTokens)
                : null,
            CachedInputTokens: Volatile.Read(ref _hasCachedInput) == 1
                ? Interlocked.Read(ref _cachedInputTokens)
                : null,
            TotalTokens: Volatile.Read(ref _hasTotal) == 1
                ? Interlocked.Read(ref _totalTokens)
                : null,
            ParseRetries: Interlocked.Read(ref _parseRetries),
            TotalDuration: TimeSpan.FromTicks(Interlocked.Read(ref _totalDurationTicks)),
            PerAdapter: perAdapter);
    }

    /// <inheritdoc/>
    public void Reset()
    {
        Interlocked.Exchange(ref _callCount, 0);
        Interlocked.Exchange(ref _inputTokens, 0);
        Interlocked.Exchange(ref _outputTokens, 0);
        Interlocked.Exchange(ref _cachedInputTokens, 0);
        Interlocked.Exchange(ref _totalTokens, 0);
        Interlocked.Exchange(ref _parseRetries, 0);
        Interlocked.Exchange(ref _totalDurationTicks, 0);
        Interlocked.Exchange(ref _hasInput, 0);
        Interlocked.Exchange(ref _hasOutput, 0);
        Interlocked.Exchange(ref _hasCachedInput, 0);
        Interlocked.Exchange(ref _hasTotal, 0);
        _byAdapter.Clear();
    }

    // Mutable per-adapter accumulator. Each numeric field guarded by
    // Interlocked; the has-value sentinels match the parent's semantics.
    private sealed class AdapterStats
    {
        private readonly string _name;
        private long _callCount;
        private long _inputTokens;
        private long _outputTokens;
        private long _cachedInputTokens;
        private long _totalTokens;
        private long _parseRetries;
        private long _totalDurationTicks;
        private int _hasInput;
        private int _hasOutput;
        private int _hasCachedInput;
        private int _hasTotal;

        public AdapterStats(string name) => _name = name;

        public void Add(LlmCallUsage usage)
        {
            Interlocked.Increment(ref _callCount);
            Interlocked.Add(ref _parseRetries, usage.ParseRetries);
            Interlocked.Add(ref _totalDurationTicks, usage.Duration.Ticks);
            if (usage.InputTokens is long i)
            {
                Interlocked.Add(ref _inputTokens, i);
                Interlocked.Exchange(ref _hasInput, 1);
            }
            if (usage.OutputTokens is long o)
            {
                Interlocked.Add(ref _outputTokens, o);
                Interlocked.Exchange(ref _hasOutput, 1);
            }
            if (usage.CachedInputTokens is long c)
            {
                Interlocked.Add(ref _cachedInputTokens, c);
                Interlocked.Exchange(ref _hasCachedInput, 1);
            }
            if (usage.TotalTokens is long t)
            {
                Interlocked.Add(ref _totalTokens, t);
                Interlocked.Exchange(ref _hasTotal, 1);
            }
        }

        public LlmAdapterStats ToReadOnlySnapshot() => new(
            Name: _name,
            CallCount: Interlocked.Read(ref _callCount),
            InputTokens: Volatile.Read(ref _hasInput) == 1
                ? Interlocked.Read(ref _inputTokens)
                : null,
            OutputTokens: Volatile.Read(ref _hasOutput) == 1
                ? Interlocked.Read(ref _outputTokens)
                : null,
            CachedInputTokens: Volatile.Read(ref _hasCachedInput) == 1
                ? Interlocked.Read(ref _cachedInputTokens)
                : null,
            TotalTokens: Volatile.Read(ref _hasTotal) == 1
                ? Interlocked.Read(ref _totalTokens)
                : null,
            ParseRetries: Interlocked.Read(ref _parseRetries),
            TotalDuration: TimeSpan.FromTicks(Interlocked.Read(ref _totalDurationTicks)));
    }
}
