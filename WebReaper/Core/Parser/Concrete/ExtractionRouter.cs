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
/// </summary>
public sealed class ExtractionRouter : IContentExtractor
{
    private readonly IContentExtractor _primary;
    private readonly IContentExtractor _fallback;
    private readonly Func<JsonObject, Schema?, bool> _isValid;
    private readonly ILogger _logger;

    /// <summary>
    /// Compose a primary and a fallback extractor with an optional
    /// validation predicate. The default predicate is
    /// <see cref="SchemaSatisfiedValidator.IsSatisfied"/> — escalates
    /// when a required schema leaf is empty or absent.
    /// </summary>
    public ExtractionRouter(
        IContentExtractor primary,
        IContentExtractor fallback,
        Func<JsonObject, Schema?, bool>? isValid = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(fallback);
        _primary = primary;
        _fallback = fallback;
        _isValid = isValid ?? SchemaSatisfiedValidator.IsSatisfied;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc/>
    public async Task<JsonObject> ExtractAsync(string document, Schema? schema)
    {
        var primaryResult = await _primary.ExtractAsync(document, schema);

        if (_isValid(primaryResult, schema))
        {
            _logger.LogInformation("Extraction router: primary extractor's result is valid; no fallback.");
            return primaryResult;
        }

        _logger.LogInformation("Extraction router: primary's result failed validation; escalating to fallback.");
        return await _fallback.ExtractAsync(document, schema);
    }
}
