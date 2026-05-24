using System.Text.Json.Nodes;

namespace WebReaper.Domain.Agent;

/// <summary>
/// The full persisted state of an in-flight agent run (ADR-0051 fork 8) —
/// written by the engine to the
/// <see cref="WebReaper.Core.Agent.Abstract.IAgentRunStore"/> after every
/// brain decision, read back on resume.
/// <para>
/// The snapshot is the entire load-bearing record of the run: the goal (so a
/// caller can resume by <c>runId</c> alone without re-supplying it), the
/// step counter, the decision history, the visited list, the records pulled
/// so far, and the current URL the engine was about to consult the brain on.
/// At-least-once on effects, exactly-once on brain decisions — see
/// ADR-0051 §Decision §6 for the persist-before-execute semantics.
/// </para>
/// </summary>
/// <param name="Goal">The natural-language goal the run was started with.</param>
/// <param name="LastDecidedStep">Zero-based index of the most recent step the
/// brain decided. On resume the engine restarts at <c>LastDecidedStep + 1</c>;
/// the prior step's <em>effect</em> (sink emission, scheduler enqueue, page
/// action) may re-run — sink idempotency is the caller's concern.</param>
/// <param name="History">Every <see cref="AgentDecision"/> the brain returned
/// on this run so far, in step order — the brain decisions are exactly-once
/// in the persisted record, so a resume sees the same history the in-flight
/// run did.</param>
/// <param name="VisitedUrls">Every URL the engine has loaded on this run so
/// far, in chronological order.</param>
/// <param name="Records">The records pulled by prior
/// <see cref="AgentDecision.Extract"/> decisions on this run. Sinks have
/// already received these; the list is the resume-time convenience copy.</param>
/// <param name="CurrentUrl">The URL the engine was about to consult the brain
/// on at the moment of the last persist — re-seeded into the scheduler on
/// resume.</param>
public sealed record AgentRunSnapshot(
    string Goal,
    int LastDecidedStep,
    IReadOnlyList<AgentDecision> History,
    IReadOnlyList<string> VisitedUrls,
    IReadOnlyList<JsonObject> Records,
    string? CurrentUrl);
