namespace WebReaper.AI.Llm;

/// <summary>
/// The accumulator <see cref="LlmCall{TResponse}"/> reports completed calls
/// into (ADR-0066). One instance per engine — the engine owns it via the
/// builder-side telemetry handle; adapters thread it through their
/// <see cref="LlmCall{TResponse}"/> constructions.
/// <para>
/// <see cref="NullLlmCallTelemetry"/> is the no-op for à-la-carte adapter
/// construction outside an engine — when an adapter is constructed
/// directly (not via the builder's <c>WithLlm*</c> extensions), the
/// telemetry argument defaults to null and the mechanism substitutes the
/// null implementation; calls are discarded.
/// </para>
/// <para>
/// Custom implementations are valid — a consumer wanting to wire LLM
/// usage to OpenTelemetry, AppInsights, or a custom store implements this
/// interface and passes their instance via the builder property
/// <c>LlmTelemetry</c> on either <c>ScraperEngineBuilder</c> or
/// <c>AgentEngineBuilder</c>.
/// </para>
/// </summary>
public interface ILlmCallTelemetry
{
    /// <summary>Record one completed call. Implementations MUST be
    /// thread-safe — called from parallel <c>ScraperEngine</c> workers.</summary>
    /// <param name="usage">The call's usage record.</param>
    void Record(LlmCallUsage usage);

    /// <summary>Read the current aggregate. Implementations MUST return
    /// an immutable point-in-time copy; concurrent
    /// <see cref="Record(LlmCallUsage)"/> calls during a snapshot are
    /// allowed but their cross-field consistency is not guaranteed (the
    /// default impl's snapshot may see a half-updated aggregate during
    /// heavy parallel traffic — acceptable; snapshots are statistical,
    /// not balance-sheet).</summary>
    LlmTelemetrySnapshot Snapshot();

    /// <summary>Clear the accumulator. Called by the engine at the start
    /// of each <c>RunAsync</c> to isolate runs from each other.</summary>
    void Reset();
}

/// <summary>The no-op <see cref="ILlmCallTelemetry"/> the mechanism uses
/// when constructed without a telemetry instance — à-la-carte adapter
/// construction outside an engine (ADR-0066).</summary>
public sealed class NullLlmCallTelemetry : ILlmCallTelemetry
{
    /// <summary>The shared singleton instance.</summary>
    public static readonly NullLlmCallTelemetry Instance = new();

    private NullLlmCallTelemetry() { }

    /// <inheritdoc/>
    public void Record(LlmCallUsage usage) { /* discard */ }

    /// <inheritdoc/>
    public LlmTelemetrySnapshot Snapshot() => LlmTelemetrySnapshot.Empty;

    /// <inheritdoc/>
    public void Reset() { /* no-op */ }
}
