using System.Text.Json.Nodes;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Abstract;

/// <summary>
/// The self-heal seam (ADR-0047): given the original
/// <see cref="Schema"/>, the document the fold ran against, and the
/// failed extraction result, propose a patched <see cref="Schema"/>
/// with corrected selectors — or <c>null</c> if no repair was
/// possible.
/// <para>
/// The default implementation lives in <c>WebReaper.AI</c>
/// (<c>LlmSelectorRepairer</c>); the consumer wires it via
/// <c>ScraperEngineBuilder.WithSelfHealing</c> or
/// <c>WithLlmSelfHealing</c>. The wrapper that drives the loop is
/// <see cref="Concrete.SelfHealingContentExtractor"/>; this seam
/// just produces the patch.
/// </para>
/// <para>
/// ADR-0062 widened the signature with an optional
/// <c>failureReason</c> argument — the <see cref="ValidationResult.Reason"/>
/// the validator emitted for the failed extraction (e.g.
/// <c>"required field 'price' is empty"</c>). LLM-backed repairers
/// inject it into the prompt so the model sees which fields need
/// repair; deterministic repairers can ignore it.
/// </para>
/// </summary>
public interface ISelectorRepairer
{
    /// <summary>
    /// Examine <paramref name="failedResult"/> against
    /// <paramref name="original"/> and the live
    /// <paramref name="document"/>; return a patched Schema with new
    /// selectors for fields that failed, or <c>null</c> if no repair
    /// was possible. The returned Schema must satisfy the ADR-0028
    /// construction guards (non-empty Field/Selector on leaves;
    /// non-empty Selector on list containers) — the wrapper does not
    /// re-validate that.
    /// <para>
    /// The optional <paramref name="failureReason"/> carries the
    /// validator's verdict (ADR-0062): a human-readable summary the
    /// repairer can inject into its prompt. Null when the call site
    /// has no reason (e.g. a transitional caller from a v10.x release
    /// before the seam, or a synthetic test).
    /// </para>
    /// </summary>
    Task<Schema?> RepairAsync(
        Schema original,
        string document,
        JsonObject failedResult,
        string? failureReason = null,
        CancellationToken cancellationToken = default);
}
