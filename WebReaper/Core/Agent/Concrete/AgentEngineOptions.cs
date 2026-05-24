namespace WebReaper.Core.Agent.Concrete;

/// <summary>
/// Hard caps the engine enforces on every agent run (ADR-0051 fork 6 + fork 7
/// verdicts — defence-in-depth termination, options-enforced not seam-based).
/// Brain ignorance is a feature: the brain doesn't see these knobs, so it
/// can't game them.
/// </summary>
/// <param name="MaxSteps">Engine-side cap on the number of brain decisions
/// per run. Defaults to <c>50</c> — generous (well above the typical run);
/// catches misbehaving brains and pathological pages, not steering. Set to
/// <see cref="int.MaxValue"/> to disable.</param>
/// <param name="MaxBudgetTokens">Engine-side cap on cumulative LLM-token
/// usage per run. Defaults to <c>null</c> (off): token-counting is per-model
/// and would lock the agent into a tokeniser dep (ADR-0050 lesson). When
/// set, the engine reads <c>ChatResponse.Usage.TotalTokenCount</c> from
/// whichever brain reports it; a chat client that doesn't surface usage
/// makes the cap silently inert (v2 may warn).</param>
public sealed record AgentEngineOptions(
    int MaxSteps = 50,
    int? MaxBudgetTokens = null);
