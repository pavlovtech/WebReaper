namespace WebReaper.Domain.Telemetry;

/// <summary>
/// The core-side surface satellites use to plug per-run telemetry into
/// <see cref="WebReaper.Core.ScraperEngine"/> and
/// <c>WebReaper.Core.Agent.Concrete.AgentEngine</c> without core taking
/// a dep on satellite-defined snapshot shapes (ADR-0066, ADR-0009
/// quarantine). The satellite-side builder constructs the record once at
/// <c>BuildAsync</c> time over its telemetry accumulator (e.g.
/// <c>WebReaper.AI.Llm.LlmCallTelemetry</c>); the engine treats every
/// callback as opaque, calling <see cref="Snapshot"/> at the end of
/// <c>RunAsync</c>, <see cref="Reset"/> at the start (to isolate runs
/// from each other when a builder is re-used), and
/// <see cref="TotalLlmTokens"/> at every step of the agent loop for the
/// <c>MaxBudgetTokens</c> check.
/// <para>
/// Field semantics: <see cref="TotalLlmTokens"/> is nullable because
/// not every satellite tracks LLM tokens — a satellite that reports
/// network bytes / page count / something else leaves it null, and the
/// agent loop's budget check becomes a no-op.
/// </para>
/// </summary>
/// <param name="Snapshot">Returns the satellite's current accumulated
/// snapshot. The engine treats it as opaque; consumer code reading
/// <see cref="RunReport.Llm"/> casts to the satellite's concrete type
/// (e.g. <c>WebReaper.AI.Llm.LlmTelemetrySnapshot</c>).</param>
/// <param name="Reset">Clears the satellite's accumulator. Called by
/// the engine at the start of each <c>RunAsync</c>; ensures consecutive
/// runs on the same builder don't share telemetry state.</param>
/// <param name="TotalLlmTokens">Optional getter for the current
/// cumulative total tokens — used by
/// <c>AgentEngineOptions.MaxBudgetTokens</c> enforcement. <c>null</c>
/// when the satellite tracks no token budget.</param>
public sealed record RunTelemetryHooks(
    Func<object?> Snapshot,
    Action Reset,
    Func<long?>? TotalLlmTokens = null);
