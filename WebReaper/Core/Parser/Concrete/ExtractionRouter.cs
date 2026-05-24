using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Concrete;

/// <summary>
/// The deterministic-first → fallback content extractor (ADR-0046).
/// Composes two <see cref="IContentExtractor"/>s; runs
/// <see cref="_primary"/>, validates its output against the schema, and
/// escalates to <see cref="_fallback"/> only on validation failure.
/// <para>
/// Itself an <see cref="IContentExtractor"/> — the seam stays a seam,
/// not a seam-of-a-seam (ADR-0001/0002 discipline). The plan
/// (REPOSITIONING-PLAN §2.1) named this "IExtractionRouter"; the
/// implementation is a class composing the existing seam, not a new
/// public interface.
/// </para>
/// <para>
/// ADR-0062 replaced the <c>Func&lt;JsonObject, Schema?, bool&gt;?</c>
/// constructor parameter with an <see cref="ISchemaValidator"/>?
/// — a custom predicate now implements the seam directly. The default
/// is <see cref="SchemaSatisfiedValidator.Instance"/> (ADR-0029 policy).
/// </para>
/// </summary>
public sealed class ExtractionRouter : IContentExtractor
{
    private readonly IContentExtractor _primary;
    private readonly IContentExtractor _fallback;
    private readonly ISchemaValidator _validator;
    private readonly ILogger _logger;

    /// <summary>
    /// Compose a primary and a fallback extractor with an optional
    /// <paramref name="validator"/>. The default validator is
    /// <see cref="SchemaSatisfiedValidator.Instance"/> — escalates
    /// when a required schema leaf is empty or absent.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="primary"/> or <paramref name="fallback"/> is null.</exception>
    public ExtractionRouter(
        IContentExtractor primary,
        IContentExtractor fallback,
        ISchemaValidator? validator = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(fallback);
        _primary = primary;
        _fallback = fallback;
        _validator = validator ?? SchemaSatisfiedValidator.Instance;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc/>
    public async Task<JsonObject> ExtractAsync(string document, Schema? schema)
    {
        var primaryResult = await _primary.ExtractAsync(document, schema);

        var verdict = _validator.Validate(primaryResult, schema);
        if (verdict.IsValid)
        {
            _logger.LogInformation("Extraction router: primary extractor's result is valid; no fallback.");
            return primaryResult;
        }

        _logger.LogInformation(
            "Extraction router: primary's result failed validation ({Reason}); escalating to fallback.",
            verdict.Reason);
        return await _fallback.ExtractAsync(document, schema);
    }
}
