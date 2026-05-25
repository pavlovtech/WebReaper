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
/// usage per run (ADR-0066 — finally enforced; previously documented but
/// silently inert). Defaults to <c>null</c> (off): token-counting is
/// per-model and would lock the agent into a tokeniser dep (ADR-0050
/// lesson). When set, the engine reads the cumulative
/// <c>LlmTelemetrySnapshot.TotalTokens</c> via the registered
/// <see cref="WebReaper.Domain.Telemetry.RunTelemetryHooks.TotalLlmTokens"/>;
/// a chat client that doesn't surface usage makes the cap silently inert,
/// and a run without any LLM adapter never triggers it. Termination
/// precedence per ADR-0051 fork 6: brain <c>Stop</c> &gt; <c>MaxSteps</c>
/// &gt; <c>MaxBudgetTokens</c> &gt; caller cancellation. Typed
/// <see cref="long"/> for headroom — large multi-page agent runs cumulate
/// past <see cref="int.MaxValue"/> tokens.</param>
public sealed record AgentEngineOptions(
    int MaxSteps = 50,
    long? MaxBudgetTokens = null);
