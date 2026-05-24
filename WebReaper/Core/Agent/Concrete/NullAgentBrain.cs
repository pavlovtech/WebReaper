using WebReaper.Core.Agent.Abstract;
using WebReaper.Domain.Agent;

namespace WebReaper.Core.Agent.Concrete;

/// <summary>
/// The no-op default <see cref="IAgentBrain"/> (ADR-0051): always returns
/// <see cref="AgentDecision.Stop"/> on the first call. The engine never
/// actually runs against this — the builder's <c>BuildAsync</c> throws
/// <see cref="InvalidOperationException"/> when the brain is still the null
/// singleton, so the misconfiguration is visible at construction (the agent
/// is *useless* without a brain — sharpened from ADR-0050's
/// <c>NullActionResolver</c> pattern, which only warns because a SemanticAct-
/// less Crawl is still useful).
/// </summary>
internal sealed class NullAgentBrain : IAgentBrain
{
    /// <summary>The shared singleton — stateless.</summary>
    public static readonly NullAgentBrain Instance = new();

    private NullAgentBrain() { }

    /// <inheritdoc/>
    public ValueTask<AgentDecision> DecideAsync(
        AgentState state,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult<AgentDecision>(
            new AgentDecision.Stop { Reason = "no IAgentBrain registered" });
}
