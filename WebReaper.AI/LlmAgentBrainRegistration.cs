using Microsoft.Extensions.AI;
using WebReaper.Builders;

namespace WebReaper.AI;

/// <summary>
/// The satellite's <see cref="LlmAgentBrain"/> registration extensions
/// (ADR-0009 pattern, ADR-0051). Wires the LLM-backed
/// <see cref="WebReaper.Core.Agent.Abstract.IAgentBrain"/> through the core's
/// <see cref="AgentEngineBuilder.WithBrain"/> seam.
/// </summary>
public static class LlmAgentBrainRegistration
{
    /// <summary>
    /// Register an LLM-backed agent brain (ADR-0051): the agent engine
    /// invokes it on every <c>DecideAsync</c> call to pick the next agent
    /// step (Extract / Follow / Act / Stop) given the bounded agent state.
    /// </summary>
    public static AgentEngineBuilder WithLlmBrain(
        this AgentEngineBuilder builder,
        IChatClient chatClient,
        LlmAgentBrainOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(chatClient);
        return builder.WithBrain(new LlmAgentBrain(chatClient, options));
    }
}
