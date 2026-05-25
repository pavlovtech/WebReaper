using System.Text.Json.Nodes;
using WebReaper.Domain.Telemetry;

namespace WebReaper.Domain.Agent;

/// <summary>
/// The terminal payload of an
/// <see cref="WebReaper.Core.Agent.Concrete.AgentEngine"/> run (ADR-0051) —
/// returned by <c>AgentEngine.RunAsync</c> /
/// <see cref="WebReaper.Agent.RunAsync(string, string,
///     WebReaper.Core.Agent.Abstract.IAgentBrain,
///     Action{WebReaper.Builders.AgentEngineBuilder}?, CancellationToken)"/> /
/// <see cref="WebReaper.Agent.ResumeAsync"/>.
/// <para>
/// The <see cref="RunId"/> is the resume handle — the caller passes it back to
/// <c>ResumeAsync</c> to continue an interrupted run. Generated on a fresh
/// run, propagated from the persisted snapshot on a resume. Holding it
/// satisfies the firecrawl-shaped <c>jobId</c> contract from the
/// <see cref="WebReaper.Core.Agent.Abstract.IAgentRunStore"/> design.
/// </para>
/// <para>
/// ADR-0066: <see cref="Report"/> carries the per-run telemetry summary
/// (LLM token aggregates, per-adapter breakdown, wall-clock duration).
/// <c>Report.Llm</c> is opaque <see cref="object"/> in core — cast to
/// <c>WebReaper.AI.Llm.LlmTelemetrySnapshot</c> when the AI satellite is
/// in use; <c>null</c> when no LLM adapter ran on the engine.
/// </para>
/// </summary>
/// <param name="RunId">The unique identifier for this run — the resume key
/// against the <see cref="WebReaper.Core.Agent.Abstract.IAgentRunStore"/>.</param>
/// <param name="Records">The accumulated records the brain extracted across
/// every <see cref="AgentDecision.Extract"/> decision, in step order. Sinks
/// have already received each record (per-Extract fan-out, fork 9); this list
/// is the in-process convenience copy.</param>
/// <param name="TerminationReason">Human-readable explanation of why the run
/// ended — the brain's <see cref="AgentDecision.Stop.Reason"/> when the brain
/// stopped, otherwise the engine's cap label
/// (<c>"MaxSteps reached"</c>, <c>"MaxBudgetTokens (X) reached (spent=Y)"</c>,
/// <c>"Scheduler drained"</c>).</param>
/// <param name="History">Every <see cref="AgentDecision"/> the brain returned
/// on this run, in step order — the audit trail for the run log.</param>
/// <param name="VisitedUrls">Every URL the engine loaded on this run, in
/// chronological order.</param>
/// <param name="StepsExecuted">Number of decisions the brain made before the
/// run ended.</param>
/// <param name="Report">Per-run telemetry summary (ADR-0066) — LLM token
/// aggregates + per-adapter breakdown + wall-clock duration.</param>
public sealed record AgentResult(
    string RunId,
    IReadOnlyList<JsonObject> Records,
    string TerminationReason,
    IReadOnlyList<AgentDecision> History,
    IReadOnlyList<string> VisitedUrls,
    int StepsExecuted,
    RunReport Report);
