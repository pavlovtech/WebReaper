using System.Text.Json.Nodes;
using WebReaper.Domain.PageActions;

namespace WebReaper.Domain.Agent;

/// <summary>
/// What happened when the engine executed the previous
/// <see cref="AgentDecision"/> (ADR-0061). The brain sees this on the
/// <see cref="AgentState.LastOutcome"/> field of the bounded
/// <see cref="AgentState"/> view on each <c>DecideAsync</c> call.
/// <para>
/// A closed sum (the ADR-0001 closed-sum pattern shared with
/// <see cref="AgentDecision"/>, <see cref="PageAction"/>, and
/// <see cref="WebReaper.Core.Crawling.CrawlOutcome"/>): exactly one of six
/// arms — <see cref="None"/>, <see cref="Extracted"/>, <see cref="Followed"/>,
/// <see cref="ActDispatched"/>, <see cref="Failed"/>, <see cref="Stopped"/> —
/// each carrying the outcome's per-arm-shaped data. Construct only via the
/// nested arms; the union is not extensible.
/// </para>
/// <para>
/// The shape is per-arm because the brain's *next* decision needs different
/// facts from each prior arm: post-Extract it wants the just-emitted record
/// and the cumulative count; post-Follow it wants the post-redirect URL and
/// HTTP status; post-Act it wants the resolved arm a SemanticAct became;
/// post-Failure it wants the failure class. The closed sum makes "which of
/// these is meaningful?" a pattern-match by construction — no per-field
/// null-checking.
/// </para>
/// </summary>
public abstract record AgentDecisionOutcome
{
    private AgentDecisionOutcome() { }

    /// <summary>First step — no prior decision was executed. The brain's
    /// first <c>DecideAsync</c> on a fresh run sees this; a resumed run's
    /// first decision sees the persisted prior outcome instead.</summary>
    public sealed record None() : AgentDecisionOutcome;

    /// <summary>The prior step's <see cref="AgentDecision.Extract"/> decision
    /// produced a record that survived the page-processor pipeline (including
    /// ADR-0062's <c>ISchemaValidator</c>). <paramref name="Record"/> is the
    /// most-recently-emitted <see cref="JsonObject"/> (or <c>null</c> when
    /// the processor pipeline dropped it); <paramref name="RecordCount"/> is
    /// the cumulative count of emitted records up to and including this
    /// step.</summary>
    /// <param name="Record">The most recently emitted record, or <c>null</c>
    /// when the processor pipeline dropped it.</param>
    /// <param name="RecordCount">Cumulative count of emitted records.</param>
    public sealed record Extracted(JsonObject? Record, int RecordCount) : AgentDecisionOutcome;

    /// <summary>The prior step's <see cref="AgentDecision.Follow"/> decision
    /// loaded a page. <paramref name="ActualUrl"/> is the post-redirect URL
    /// the page loader settled on (may differ from the brain's proposed URL);
    /// <paramref name="StatusCode"/> is the HTTP status (200 for ok, 404 for
    /// not-found, …). 0 when the page type is Dynamic — the browser transport
    /// doesn't surface a single HTTP status per page; the brain reads "0
    /// means dynamic" from the system prompt.</summary>
    /// <param name="ActualUrl">The post-redirect URL the loader settled on.</param>
    /// <param name="StatusCode">HTTP status code, or 0 for dynamic pages.</param>
    public sealed record Followed(string ActualUrl, int StatusCode) : AgentDecisionOutcome;

    /// <summary>The prior step's <see cref="AgentDecision.Act"/> decision
    /// dispatched a page action. <paramref name="ResolvedAction"/> is the
    /// concrete arm that ran — a <see cref="PageAction.SemanticAct"/> resolves
    /// to a concrete <see cref="PageAction.Click"/> / <see cref="PageAction.Wait"/>
    /// / etc. via ADR-0050's resolver; the brain sees what its intent
    /// became.</summary>
    /// <param name="ResolvedAction">The concrete action arm that ran.</param>
    public sealed record ActDispatched(PageAction ResolvedAction) : AgentDecisionOutcome;

    /// <summary>The prior step failed mid-execution. <paramref name="Reason"/>
    /// is a human-readable summary; <paramref name="ExceptionType"/> is the
    /// .NET type name (e.g. <c>HttpRequestException</c>,
    /// <c>SemanticActResolutionException</c>) so the brain can branch on
    /// failure class without a full exception object. <c>null</c> when the
    /// failure is a structural rejection rather than a thrown exception
    /// (e.g. <c>"already visited"</c>).</summary>
    /// <param name="Reason">Human-readable failure summary.</param>
    /// <param name="ExceptionType">The .NET exception type name, or
    /// <c>null</c> for structural failures.</param>
    public sealed record Failed(string Reason, string? ExceptionType) : AgentDecisionOutcome;

    /// <summary>End-state marker — written to the final snapshot when the
    /// engine breaks out of the loop. The brain never sees this in
    /// <see cref="AgentState"/> (the loop has already terminated); resume
    /// tooling reads it on a completed run.</summary>
    /// <param name="Reason">The termination reason.</param>
    public sealed record Stopped(string Reason) : AgentDecisionOutcome;
}
