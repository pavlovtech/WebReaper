using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Concrete;

/// <summary>
/// The self-healing wrapper (ADR-0047). Composes a primary
/// <see cref="IContentExtractor"/> (typically the deterministic
/// <c>SchemaFold</c>) with an <see cref="ISelectorRepairer"/>. On a
/// failed deterministic pass:
/// <list type="number">
/// <item>Ask the repairer for a patched Schema (passing the validator's
/// failure reason — ADR-0062 — for the repairer to inject into its
/// prompt).</item>
/// <item>Re-run the primary with the patched Schema.</item>
/// <item>If that succeeds, cache the patched Schema and serve every
/// subsequent page of the Crawl from the deterministic fast path —
/// no further LLM cost.</item>
/// </list>
/// <para>
/// The cache key is the original <see cref="Schema"/> by reference
/// identity (one patch per Schema instance — the common
/// one-Schema-per-Crawl case). Per-host keying is a future
/// enhancement.
/// </para>
/// <para>
/// ADR-0062 added the optional <see cref="ISchemaValidator"/>
/// constructor argument (default <see cref="SchemaSatisfiedValidator.Instance"/>).
/// Both validation call sites — the initial primary pass and the
/// re-validation after a repair — consult the seam.
/// </para>
/// </summary>
public sealed class SelfHealingContentExtractor : IContentExtractor
{
    private readonly IContentExtractor _primary;
    private readonly ISelectorRepairer _repairer;
    private readonly ISchemaValidator _validator;
    private readonly ILogger _logger;

    // Reference-identity cache: one patched Schema per original-
    // Schema instance. The wrapper is single-instance-per-Crawl by
    // design, so this stays bounded.
    private readonly ConcurrentDictionary<Schema, Schema> _patchedCache = new();

    /// <summary>
    /// Compose a primary extractor with a repairer. The wrapper's
    /// per-instance cache stores patched Schemas by reference identity
    /// — one entry per original Schema instance.
    /// </summary>
    /// <param name="primary">The deterministic extractor whose failure
    /// triggers repair.</param>
    /// <param name="repairer">The selector repairer asked for a
    /// patched Schema on failure.</param>
    /// <param name="validator">Optional schema validator (ADR-0062).
    /// Defaults to <see cref="SchemaSatisfiedValidator.Instance"/>.</param>
    /// <param name="logger">Optional logger.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="primary"/> or <paramref name="repairer"/> is null.</exception>
    public SelfHealingContentExtractor(
        IContentExtractor primary,
        ISelectorRepairer repairer,
        ISchemaValidator? validator = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(repairer);
        _primary = primary;
        _repairer = repairer;
        _validator = validator ?? SchemaSatisfiedValidator.Instance;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc/>
    public async Task<JsonObject> ExtractAsync(string document, Schema? schema)
    {
        // Markdown / LLM extractors call with a null schema; nothing
        // to self-heal. Pass through to the primary.
        if (schema is null) return await _primary.ExtractAsync(document, null);

        // Cache hit: a prior page's repair already replaced this
        // schema. Run primary with the patch.
        if (_patchedCache.TryGetValue(schema, out var cachedPatch))
        {
            _logger.LogInformation("Self-heal: serving cached patched schema");
            return await _primary.ExtractAsync(document, cachedPatch);
        }

        // Cache miss — first try the original Schema.
        var result = await _primary.ExtractAsync(document, schema);
        var verdict = _validator.Validate(result, schema);

        if (verdict.IsValid)
        {
            // Deterministic path succeeded — no repair needed.
            return result;
        }

        _logger.LogInformation(
            "Self-heal: primary failed validation ({Reason}); asking the repairer",
            verdict.Reason);

        // Ask the repairer for a patch — pass the failure reason so an
        // LLM-backed repairer can put it in the prompt.
        var patched = await _repairer.RepairAsync(schema, document, result, verdict.Reason);
        if (patched is null)
        {
            _logger.LogInformation("Self-heal: repairer returned null; falling back to the failed result");
            return result;
        }

        // Validate the patch by re-running the primary.
        var patchedResult = await _primary.ExtractAsync(document, patched);
        var patchedVerdict = _validator.Validate(patchedResult, patched);

        if (patchedVerdict.IsValid)
        {
            // Patch verified — cache it for the rest of the Crawl.
            _patchedCache[schema] = patched;
            _logger.LogInformation("Self-heal: repair verified and cached");
        }
        else
        {
            _logger.LogInformation(
                "Self-heal: patched schema still failed validation ({Reason}); not caching",
                patchedVerdict.Reason);
        }

        return patchedResult;
    }
}
