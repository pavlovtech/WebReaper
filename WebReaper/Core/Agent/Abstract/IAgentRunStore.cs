using WebReaper.Domain.Agent;

namespace WebReaper.Core.Agent.Abstract;

/// <summary>
/// The durable agent-state seam (ADR-0051 §Decision §6) — sibling to
/// <see cref="WebReaper.Core.Scheduler.Abstract.IScheduler"/>,
/// <see cref="WebReaper.Core.LinkTracker.Abstract.IVisitedLinkTracker"/>,
/// <see cref="WebReaper.ConfigStorage.Abstract.IScraperConfigStorage"/>, and
/// <see cref="WebReaper.Core.CookieStorage.Abstract.ICookiesStorage"/>. Stores the
/// full <see cref="AgentRunSnapshot"/> of an in-flight agent run keyed by
/// <c>runId</c> so a process restart can resume the run from
/// <see cref="AgentRunSnapshot.LastDecidedStep"/> + 1.
/// <para>
/// The default registration is the in-memory <c>InMemoryAgentRunStore</c>;
/// core also ships <c>FileAgentRunStore</c>. Satellite adapters in lockstep:
/// <c>WebReaper.Redis</c>, <c>WebReaper.Mongo</c>, <c>WebReaper.Sqlite</c>,
/// <c>WebReaper.Cosmos</c>. <c>WebReaper.AzureServiceBus</c> is queue-shaped
/// and intentionally skipped (a snapshot store needs key-value lookup, not
/// FIFO delivery).
/// </para>
/// <para>
/// <b>Persist-before-execute semantics.</b> The engine calls
/// <see cref="SaveStepAsync"/> with the brain's
/// <see cref="AgentDecision"/> <em>before</em> executing it (sink emission,
/// scheduler enqueue, page action). On crash mid-execute, resume re-executes
/// the last persisted decision — sinks may see a duplicate record for that
/// step (caller's sink-idempotency concern; the change-tracking processor
/// ADR-0048 deduplicates on hash so it composes cleanly). Brain decisions are
/// exactly-once in the persisted history.
/// </para>
/// </summary>
public interface IAgentRunStore
{
    /// <summary>
    /// Load the persisted snapshot for <paramref name="runId"/>, or
    /// <c>null</c> when no snapshot exists (a fresh run). The engine resumes
    /// from the snapshot when present and starts fresh otherwise.
    /// </summary>
    ValueTask<AgentRunSnapshot?> LoadAsync(
        string runId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persist <paramref name="postState"/> as the new snapshot for
    /// <paramref name="runId"/>. The engine calls this BEFORE executing
    /// <paramref name="decision"/>; the <paramref name="postState"/>'s
    /// <see cref="AgentRunSnapshot.LastDecidedStep"/> already includes the
    /// decision being persisted (so a resume restarts at
    /// <c>LastDecidedStep + 1</c>).
    /// </summary>
    /// <param name="runId">The run identifier.</param>
    /// <param name="decision">The decision the brain just returned for this
    /// step — included for adapters that log per-step (e.g. SQLite's
    /// <c>run_steps</c> table); the canonical record lives in
    /// <paramref name="postState"/>'s
    /// <see cref="AgentRunSnapshot.History"/>.</param>
    /// <param name="postState">The full snapshot to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask SaveStepAsync(
        string runId,
        AgentDecision decision,
        AgentRunSnapshot postState,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Discard the snapshot for <paramref name="runId"/>. Called by the
    /// engine after a clean termination (the brain returned Stop, or the
    /// scheduler drained); a subsequent
    /// <see cref="WebReaper.Builders.AgentEngineBuilder.WithRunId"/> with
    /// the same <paramref name="runId"/> starts a fresh run.
    /// </summary>
    ValueTask DeleteAsync(
        string runId,
        CancellationToken cancellationToken = default);
}
