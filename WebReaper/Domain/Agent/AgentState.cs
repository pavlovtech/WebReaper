using System.Text.Json.Nodes;

namespace WebReaper.Domain.Agent;

/// <summary>
/// The bounded view of the agent's run that the
/// <see cref="WebReaper.Core.Agent.Abstract.IAgentBrain"/> sees on each
/// <c>DecideAsync</c> call (ADR-0051, extended by ADR-0061). Built fresh per
/// step by the engine; the brain reads, never writes.
/// <para>
/// All collection-shaped fields are <em>capped</em> (fork 3 verdict — bounded
/// history, bounded visited list, bounded candidate URLs, bounded current-page
/// markdown). The caps live on the
/// <see cref="WebReaper.AI.LlmAgentBrainOptions"/> so callers can widen them
/// for richer brains. Token cost is the constraint; unbounded prompts are the
/// first cause of cost runaway on long runs.
/// </para>
/// </summary>
/// <param name="Goal">The natural-language goal supplied at run start —
/// the brain's invariant target across every step (fork 2 verdict —
/// string, not structured).</param>
/// <param name="CurrentUrl">The URL of the currently-loaded page.</param>
/// <param name="CurrentPageMarkdown">The current page's content, pre-cleaned
/// to LLM-ready Markdown via the same ADR-0040 path the
/// <see cref="WebReaper.Core.Parser.Concrete.MarkdownContentExtractor"/>
/// produces, capped at the brain options' <c>MaxPageMarkdownChars</c>.</param>
/// <param name="CandidateUrls">The top <c>&lt;a&gt;</c> hrefs the brain may
/// consider as the next <see cref="AgentDecision.Follow"/> target, capped at
/// the brain options' <c>CandidateUrlCap</c>.</param>
/// <param name="Extracted">The records pulled by prior
/// <see cref="AgentDecision.Extract"/> decisions on this run. Capped only by
/// the brain's own cumulative record count — the engine never trims it.</param>
/// <param name="History">The last N
/// <see cref="AgentDecision"/>s the brain returned, capped at
/// <c>HistoryWindow</c>. Lets the brain see what it has been doing without
/// re-summarising.</param>
/// <param name="VisitedUrls">The last N URLs the engine has loaded for the
/// brain so far, capped at <c>VisitedWindow</c>. Belt-and-braces with the
/// engine's enforcement (fork 12) — the brain sees what's visited and avoids
/// re-proposing.</param>
/// <param name="StepNumber">Zero-based index of the current step — the engine
/// increments after each non-Stop decision.</param>
/// <param name="LastOutcome">What happened when the engine executed the
/// previous <see cref="AgentDecision"/> (ADR-0061). The closed sum has six
/// arms; first-step brains see <see cref="AgentDecisionOutcome.None"/>. The
/// brain pattern-matches the arms to decide its next move. Defaults to
/// <see cref="AgentDecisionOutcome.None"/> so callers constructing states
/// directly (tests, custom adapters) don't have to thread it through every
/// site — the engine always supplies it explicitly.</param>
public sealed record AgentState(
    string Goal,
    string CurrentUrl,
    string CurrentPageMarkdown,
    IReadOnlyList<string> CandidateUrls,
    IReadOnlyList<JsonObject> Extracted,
    IReadOnlyList<AgentDecision> History,
    IReadOnlyList<string> VisitedUrls,
    int StepNumber,
    AgentDecisionOutcome? LastOutcome = null)
{
    /// <summary>The prior step's outcome (ADR-0061). Constructor accepts
    /// <c>null</c> and normalises to <see cref="AgentDecisionOutcome.None"/>
    /// so callers (tests, custom adapters) get a working default and the
    /// brain never needs to null-check.</summary>
    public AgentDecisionOutcome LastOutcome { get; init; } = LastOutcome ?? new AgentDecisionOutcome.None();
}
