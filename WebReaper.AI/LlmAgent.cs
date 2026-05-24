using Microsoft.Extensions.AI;
using WebReaper.Builders;
using WebReaper.Core.Agent.Abstract;
using WebReaper.Domain.Agent;

namespace WebReaper.AI;

/// <summary>
/// The maximally-AI-first satellite sugar (ADR-0051 §Decision §5, HITL
/// refinement 2026-05-24) — sibling to core
/// <see cref="WebReaper.Agent"/>'s static <see cref="Agent.RunAsync"/> /
/// <see cref="Agent.ResumeAsync"/>, but accepting an <see cref="IChatClient"/>
/// directly so callers with the AI satellite never construct
/// <see cref="LlmAgentBrain"/> themselves. The firecrawl-shaped three-arg
/// one-liner: <c>await LlmAgent.RunAsync(url, goal, chatClient);</c>.
/// </summary>
public static class LlmAgent
{
    /// <summary>
    /// Run a fresh LLM-backed agent loop end-to-end. Equivalent to:
    /// <c>AgentEngineBuilder.Start(startUrl, goal).WithLlmBrain(chatClient)
    /// [.Configure...].BuildAsync().RunAsync(ct)</c>.
    /// </summary>
    public static async Task<AgentResult> RunAsync(
        string startUrl,
        string goal,
        IChatClient chatClient,
        LlmAgentBrainOptions? brainOptions = null,
        Action<AgentEngineBuilder>? configure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(startUrl);
        ArgumentException.ThrowIfNullOrEmpty(goal);
        ArgumentNullException.ThrowIfNull(chatClient);

        var builder = AgentEngineBuilder.Start(startUrl, goal)
            .WithLlmBrain(chatClient, brainOptions);
        configure?.Invoke(builder);
        var engine = await builder.BuildAsync();
        return await engine.RunAsync(cancellationToken);
    }

    /// <summary>
    /// Resume an interrupted LLM-backed agent run. Same semantics as
    /// <see cref="WebReaper.Agent.ResumeAsync"/> but with the LlmAgentBrain
    /// constructed from <paramref name="chatClient"/> internally.
    /// </summary>
    public static async Task<AgentResult> ResumeAsync(
        string runId,
        IChatClient chatClient,
        IAgentRunStore store,
        LlmAgentBrainOptions? brainOptions = null,
        Action<AgentEngineBuilder>? configure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(runId);
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(store);

        return await Agent.ResumeAsync(
            runId,
            new LlmAgentBrain(chatClient, brainOptions),
            store,
            configure,
            cancellationToken);
    }
}
