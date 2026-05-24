using WebReaper.Builders;
using WebReaper.Core.Agent.Abstract;
using WebReaper.Domain.Agent;

namespace WebReaper;

/// <summary>
/// Static one-line sugar over <see cref="AgentEngineBuilder"/> (ADR-0051
/// §Decision §4) — for the firecrawl-shaped caller who has a brain and a
/// goal and doesn't need configuration ceremony. The satellite's
/// <c>WebReaper.AI.LlmAgent</c> is the brain-less sibling that takes an
/// <c>IChatClient</c> directly (ADR-0051 §Decision §5 — the
/// maximally-AI-first surface).
/// </summary>
public static class Agent
{
    /// <summary>
    /// Run a fresh agent loop end-to-end. Equivalent to:
    /// <c>AgentEngineBuilder.Start(startUrl, goal).WithBrain(brain)[.Configure...]
    /// .BuildAsync().RunAsync(ct)</c>.
    /// </summary>
    public static async Task<AgentResult> RunAsync(
        string startUrl,
        string goal,
        IAgentBrain brain,
        Action<AgentEngineBuilder>? configure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(startUrl);
        ArgumentException.ThrowIfNullOrEmpty(goal);
        ArgumentNullException.ThrowIfNull(brain);

        var builder = AgentEngineBuilder.Start(startUrl, goal).WithBrain(brain);
        configure?.Invoke(builder);
        var engine = await builder.BuildAsync();
        return await engine.RunAsync(cancellationToken);
    }

    /// <summary>
    /// Resume an interrupted agent run. Reads the snapshot for
    /// <paramref name="runId"/> from <paramref name="store"/>, restores the
    /// goal and current URL from it, and continues from the last persisted
    /// step. The decision history is preserved exactly-once (the brain sees
    /// what it previously decided in <see cref="AgentState.History"/>);
    /// effect execution is at-least-once — sinks may see duplicate records
    /// for the resumed step (caller's sink-idempotency concern). See
    /// ADR-0051 §Decision §6 for the persist-before-execute semantics.
    /// </summary>
    public static async Task<AgentResult> ResumeAsync(
        string runId,
        IAgentBrain brain,
        IAgentRunStore store,
        Action<AgentEngineBuilder>? configure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(runId);
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(store);

        var snapshot = await store.LoadAsync(runId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"No agent snapshot found for runId '{runId}' in the supplied IAgentRunStore.");

        // The snapshot's CurrentUrl seeds the engine; if it's null (only the
        // start-URL case before any Follow), fall back to a placeholder
        // — the engine immediately overrides via the snapshot's restoration
        // logic.
        var seedUrl = snapshot.CurrentUrl ?? "https://invalid.local/";
        var builder = AgentEngineBuilder.Start(seedUrl, snapshot.Goal)
            .WithBrain(brain)
            .WithRunStore(store)
            .WithRunId(runId);
        configure?.Invoke(builder);
        var engine = await builder.BuildAsync();
        return await engine.RunAsync(cancellationToken);
    }
}
