using WebReaper.Domain.PageActions;
using WebReaper.Domain.Parsing;

namespace WebReaper.Domain.Agent;

/// <summary>
/// One step the Agent driver takes (ADR-0051): what to do next on the current
/// page, returned by the <see cref="WebReaper.Core.Agent.Abstract.IAgentBrain"/>
/// for the engine to execute.
/// <para>
/// A closed sum (the ADR-0001 closed-sum pattern shared with
/// <see cref="WebReaper.Core.Crawling.CrawlOutcome"/> and
/// <see cref="PageAction"/>): exactly one of the four nested arms — Extract /
/// Follow / Act / Stop — each carrying its own typed parameters plus a
/// <c>Reason</c> string the brain uses to explain itself for the audit trail
/// (the <see cref="AgentResult.History"/> log). Construct only via the nested
/// arms; the union is not extensible.
/// </para>
/// </summary>
public abstract record AgentDecision
{
    private AgentDecision() { }

    /// <summary>The brain's rationale for this decision — surfaced in
    /// <see cref="AgentResult.History"/> for the audit-trail-clean run log.</summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Extract a record from the current page using <paramref name="Schema"/>.
    /// The brain may evolve the schema across steps (fork 4 verdict —
    /// brain-chosen, not pre-fixed). The engine runs the schema through the
    /// registered <see cref="WebReaper.Core.Parser.Abstract.IContentExtractor"/>
    /// (ADR-0039) and fans the resulting record through the page-processor
    /// pipeline (ADR-0038) to the sinks — one Extract decision, one record
    /// emission, the per-Extract fan-out pattern of fork 9.
    /// </summary>
    /// <param name="Schema">The schema the brain wants the extractor to honour.</param>
    public sealed record Extract(Schema Schema) : AgentDecision;

    /// <summary>
    /// Load <paramref name="Url"/> as the next agent step. The engine honours
    /// the <see cref="WebReaper.Core.LinkTracker.Abstract.IVisitedLinkTracker"/>
    /// idempotency authority (fork 12 verdict — honour + expose + enforce); a
    /// Follow targeting an already-visited URL is rejected and the brain is
    /// re-asked.
    /// </summary>
    /// <param name="Url">The absolute URL to load next.</param>
    public sealed record Follow(string Url) : AgentDecision;

    /// <summary>
    /// Perform a <paramref name="Action"/> on the current page, then re-ask the
    /// brain on the post-action page state — the "see, then act" pattern
    /// (fork 1 verdict — separate Act arm, not action-on-Follow). Composes
    /// with ADR-0050: when <paramref name="Action"/> is a
    /// <see cref="PageAction.SemanticAct"/>, the registered
    /// <see cref="WebReaper.Core.Actions.Abstract.IActionResolver"/> resolves
    /// the intent the same way it does on the Crawl path — cache and all.
    /// </summary>
    /// <param name="Action">The action to dispatch on the current page.</param>
    public sealed record Act(PageAction Action) : AgentDecision;

    /// <summary>
    /// Terminate the agent run. The accumulated records (the
    /// <see cref="AgentResult.Records"/>) are the final result and the
    /// <see cref="AgentResult.TerminationReason"/> on the result is the
    /// inherited <see cref="Reason"/>.
    /// </summary>
    public sealed record Stop : AgentDecision;
}
