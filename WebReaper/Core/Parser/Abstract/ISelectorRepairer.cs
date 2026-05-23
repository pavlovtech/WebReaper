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
    /// </summary>
    Task<Schema?> RepairAsync(
        Schema original,
        string document,
        JsonObject failedResult,
        CancellationToken cancellationToken = default);
}
