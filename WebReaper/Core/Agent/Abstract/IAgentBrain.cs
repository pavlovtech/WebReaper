using WebReaper.Domain.Agent;

namespace WebReaper.Core.Agent.Abstract;

/// <summary>
/// The page-selection seam (ADR-0051): given the agent's current
/// <see cref="AgentState"/>, decide what the engine should do next — Extract a
/// record, Follow a URL, Act on the current page, or Stop the run. The fourth
/// dock for the proposer-validator pattern (after extraction-routing
/// ADR-0046, extraction-self-healing ADR-0047, and semantic actions
/// ADR-0050).
/// <para>
/// The brain is the only collaborator the agent driver demands beyond the
/// <see cref="IAgentRunStore"/> snapshot store — every other piece (loader,
/// cache, content extractor, action resolver, sinks, processors, visited-link
/// tracker) is reused from the Crawl driver unchanged. The default
/// registration is the no-op <c>NullAgentBrain</c> (returns Stop on first
/// call); the LLM-backed implementation ships in the <c>WebReaper.AI</c>
/// satellite as <c>LlmAgentBrain</c>.
/// </para>
/// <para>
/// Decisions are sequential by design (fork 11 verdict) — the brain's next
/// decision depends on the previous step's effect, so parallel
/// <c>DecideAsync</c> calls would break the proposer-validator loop. The
/// engine never invokes the brain concurrently.
/// </para>
/// </summary>
public interface IAgentBrain
{
    /// <summary>
    /// Inspect <paramref name="state"/> and return the next
    /// <see cref="AgentDecision"/>. The brain is expected to consult the
    /// state's <see cref="AgentState.History"/> /
    /// <see cref="AgentState.VisitedUrls"/> /
    /// <see cref="AgentState.Extracted"/> to avoid proposing redundant work.
    /// </summary>
    /// <param name="state">The bounded view of the run the engine built for
    /// this step.</param>
    /// <param name="cancellationToken">Cancellation token honoured by the
    /// engine if the caller cancels the run.</param>
    /// <returns>The agent's next move. The engine validates the return
    /// (e.g. a Follow targeting an already-visited URL is rejected and the
    /// brain is re-asked on the next loop iteration).</returns>
    ValueTask<AgentDecision> DecideAsync(
        AgentState state,
        CancellationToken cancellationToken = default);
}
