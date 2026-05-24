using System.Text.Json.Nodes;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Abstract;

/// <summary>
/// The schema-validator seam (ADR-0062). Asked "did the extractor's
/// output satisfy the <see cref="Schema"/>?", returns a structured verdict.
/// <para>
/// Three docks in the pipeline consume one validator instance:
/// the <see cref="Concrete.ExtractionRouter"/> (between primary and
/// fallback, ADR-0046), the
/// <see cref="Concrete.SelfHealingContentExtractor"/> (between the primary
/// pass and the repair invocation, ADR-0047), and the agent driver
/// after each <c>Extract</c> decision (ADR-0061 composition). Swap the
/// policy via <c>ScraperEngineBuilder.WithSchemaValidator</c>.
/// </para>
/// <para>
/// The default implementation is
/// <see cref="Concrete.SchemaSatisfiedValidator"/>: the ADR-0029-aligned
/// "every required leaf non-empty" rule (integer 0 / boolean false count
/// as valid; only string-empty / list-empty triggers). Alternative
/// validators include "at least N records," LLM-graded semantic
/// correctness, or any consumer rule.
/// </para>
/// </summary>
public interface ISchemaValidator
{
    /// <summary>
    /// Validate <paramref name="extracted"/> against <paramref name="schema"/>.
    /// Returns <see cref="ValidationResult.IsValid"/> = <c>true</c> when the
    /// extraction satisfies the schema for this validator's policy;
    /// otherwise <c>false</c> with a populated <see cref="ValidationResult.Reason"/>.
    /// A null <paramref name="schema"/> is the strategy-local "no schema
    /// available" case — the default validator treats it as trivially valid;
    /// custom validators may treat it differently. A null
    /// <paramref name="extracted"/> is likewise treated as trivially valid by
    /// the default — there was no record to check.
    /// </summary>
    ValidationResult Validate(JsonObject? extracted, Schema? schema);
}

/// <summary>
/// The closed-sum verdict of an <see cref="ISchemaValidator"/>: either
/// valid (no reason) or invalid (with a human-readable reason naming the
/// first failing field path or the structural failure). The
/// <see cref="Reason"/> is null exactly when <see cref="IsValid"/> is true.
/// </summary>
public sealed record ValidationResult(bool IsValid, string? Reason)
{
    /// <summary>The canonical valid verdict — no reason.</summary>
    public static ValidationResult Valid { get; } = new(true, null);

    /// <summary>Construct an invalid verdict with the given
    /// <paramref name="reason"/> (a human-readable summary the next step
    /// in the pipeline — repairer prompt, agent brain feedback — can
    /// consume).</summary>
    public static ValidationResult Invalid(string reason) => new(false, reason);
}
