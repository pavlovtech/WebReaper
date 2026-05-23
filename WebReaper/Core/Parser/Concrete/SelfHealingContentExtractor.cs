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
/// <item>Ask the repairer for a patched Schema.</item>
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
/// </summary>
public sealed class SelfHealingContentExtractor : IContentExtractor
{
    private readonly IContentExtractor _primary;
    private readonly ISelectorRepairer _repairer;
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
    public SelfHealingContentExtractor(
        IContentExtractor primary,
        ISelectorRepairer repairer,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(repairer);
        _primary = primary;
        _repairer = repairer;
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

        if (SchemaSatisfiedValidator.IsSatisfied(result, schema))
        {
            // Deterministic path succeeded — no repair needed.
            return result;
        }

        _logger.LogInformation("Self-heal: primary failed validation; asking the repairer");

        // Ask the repairer for a patch.
        var patched = await _repairer.RepairAsync(schema, document, result);
        if (patched is null)
        {
            _logger.LogInformation("Self-heal: repairer returned null; falling back to the failed result");
            return result;
        }

        // Validate the patch by re-running the primary.
        var patchedResult = await _primary.ExtractAsync(document, patched);

        if (SchemaSatisfiedValidator.IsSatisfied(patchedResult, patched))
        {
            // Patch verified — cache it for the rest of the Crawl.
            _patchedCache[schema] = patched;
            _logger.LogInformation("Self-heal: repair verified and cached");
        }
        else
        {
            _logger.LogInformation("Self-heal: patched schema still failed validation; not caching");
        }

        return patchedResult;
    }
}
