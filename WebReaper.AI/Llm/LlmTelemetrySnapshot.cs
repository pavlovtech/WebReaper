namespace WebReaper.AI.Llm;

/// <summary>
/// Immutable point-in-time aggregate of LLM call usage on a run (ADR-0066).
/// Returned by <see cref="ILlmCallTelemetry.Snapshot"/>; surfaces through
/// <c>WebReaper.Domain.Telemetry.RunReport.Llm</c> as <see cref="object"/>
/// (the satellite quarantine — consumers cast back to this type when the
/// AI satellite is referenced).
/// </summary>
/// <param name="CallCount">Total number of <see cref="LlmCall{TResponse}.InvokeAsync"/>
/// invocations reported via <see cref="ILlmCallTelemetry.Record(LlmCallUsage)"/>.</param>
/// <param name="InputTokens">Sum of <see cref="LlmCallUsage.InputTokens"/>
/// across calls; <c>null</c> when no call surfaced a value.</param>
/// <param name="OutputTokens">Sum of <see cref="LlmCallUsage.OutputTokens"/>
/// across calls; <c>null</c> when no call surfaced a value.</param>
/// <param name="CachedInputTokens">Sum of
/// <see cref="LlmCallUsage.CachedInputTokens"/> across calls; <c>null</c>
/// when no provider surfaced cache details (or no call did).</param>
/// <param name="TotalTokens">Sum of <see cref="LlmCallUsage.TotalTokens"/>
/// across calls; <c>null</c> when no call surfaced a value. Read by the
/// <c>AgentEngineOptions.MaxBudgetTokens</c> enforcement loop in the agent
/// engine (ADR-0066 §10).</param>
/// <param name="ParseRetries">Sum of bounded parse retries across all
/// calls — operational signal (high counts indicate model-output drift).</param>
/// <param name="TotalDuration">Sum of <see cref="LlmCallUsage.Duration"/>
/// across calls. Not the same as the run's wall-clock duration (calls
/// overlap under <c>ScraperEngine</c>'s parallel workers); useful as a
/// "what was the LLM portion of total work?" diagnostic.</param>
/// <param name="PerAdapter">Per-descriptor-name breakdown, keyed by
/// <see cref="LlmCallDescriptor{TResponse}.Name"/>. Empty when no calls
/// were recorded.</param>
public sealed record LlmTelemetrySnapshot(
    long CallCount,
    long? InputTokens,
    long? OutputTokens,
    long? CachedInputTokens,
    long? TotalTokens,
    long ParseRetries,
    TimeSpan TotalDuration,
    IReadOnlyDictionary<string, LlmAdapterStats> PerAdapter)
{
    /// <summary>The empty snapshot — zero calls, null aggregates, empty
    /// per-adapter dict. Returned by <see cref="NullLlmCallTelemetry.Snapshot"/>
    /// and by <see cref="LlmCallTelemetry.Snapshot"/> when no calls have
    /// been recorded yet.</summary>
    public static readonly LlmTelemetrySnapshot Empty = new(
        CallCount: 0,
        InputTokens: null,
        OutputTokens: null,
        CachedInputTokens: null,
        TotalTokens: null,
        ParseRetries: 0,
        TotalDuration: TimeSpan.Zero,
        PerAdapter: new Dictionary<string, LlmAdapterStats>());
}

/// <summary>Per-descriptor-name slice of <see cref="LlmTelemetrySnapshot"/>
/// — the run's cost / call breakdown by adapter (ADR-0066). Each field
/// mirrors the parent snapshot's field but scoped to the named adapter.</summary>
/// <param name="Name">The descriptor's name — typically the adapter class
/// name (<c>nameof(LlmContentExtractor)</c>, etc.).</param>
/// <param name="CallCount">Calls reported with this descriptor name.</param>
/// <param name="InputTokens">Sum across this adapter's calls.</param>
/// <param name="OutputTokens">Sum across this adapter's calls.</param>
/// <param name="CachedInputTokens">Sum across this adapter's calls.</param>
/// <param name="TotalTokens">Sum across this adapter's calls.</param>
/// <param name="ParseRetries">Sum across this adapter's calls.</param>
/// <param name="TotalDuration">Sum across this adapter's calls.</param>
public sealed record LlmAdapterStats(
    string Name,
    long CallCount,
    long? InputTokens,
    long? OutputTokens,
    long? CachedInputTokens,
    long? TotalTokens,
    long ParseRetries,
    TimeSpan TotalDuration);
