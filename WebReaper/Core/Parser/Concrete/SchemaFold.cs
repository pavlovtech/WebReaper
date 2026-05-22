using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Concrete;

/// <summary>
/// The deterministic adapter of the <see cref="Abstract.IContentExtractor"/>
/// content-extraction seam (ADR-0039): the single recursive
/// <see cref="Schema"/> fold, shared by every backend. It owns the grammar
/// the parsers used to each re-implement:
/// the container / object-list / leaf / value-list branching, the
/// <see cref="SchemaElement.Type"/> coercion, the missing-node →
/// log → empty-string policy, and the swallow-and-log scope (only a
/// leaf assignment is guarded; a container whose list selector is
/// missing still throws, exactly as before). Everything document-shaped
/// is delegated to <see cref="ISchemaBackend{TNode}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Public and generic on purpose: a new backend is
/// <c>new SchemaFold&lt;TNode&gt;(myBackend, logger)</c> passed
/// to <c>WithContentExtractor</c>, reusing this proven fold rather than
/// copying it. See docs/adr/0002.
/// </para>
/// <para>
/// ADR 0008: the fold's terminal projection is
/// <see cref="System.Text.Json.Nodes.JsonObject"/> (the typed
/// <see cref="ExtractAsync"/>). The legacy Newtonsoft <c>JObject</c>
/// path and <c>IContentParser</c> were removed at the 6.0.0 major; this
/// fold carries no Newtonsoft (the JSON backend's JToken→JsonNode bridge is
/// backend-local in <c>JsonSchemaBackend.ExtractRaw</c>).
/// </para>
/// </remarks>
public class SchemaFold<TNode> : IContentExtractor where TNode : class
{
    private readonly ISchemaBackend<TNode> _backend;
    private readonly ILogger _logger;

    /// <summary>Reuse the proven fold with a custom
    /// <paramref name="backend"/> — the ADR-0002 extension point:
    /// <c>new SchemaFold&lt;TNode&gt;(myBackend, logger)</c> passed to
    /// <c>WithContentExtractor</c>.</summary>
    public SchemaFold(ISchemaBackend<TNode> backend, ILogger logger)
    {
        _backend = backend;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<JsonObject> ExtractAsync(string document, Schema? schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var root = await _backend.RootAsync(document);

        try
        {
            var output = new JsonObject();

            foreach (var item in schema.Children) FillOutput(output, root, item);

            return output;
        }
        finally
        {
            // AngleSharp's IDocument is IDisposable and must outlive the
            // fold; a JToken root is not. Mirrors the old `using var doc`.
            (root as IDisposable)?.Dispose();
        }
    }

    // scope is the node selectors are evaluated against: the root at the
    // top level, or a single list-item node when recursing into a list of
    // objects (issue #28).
    private void FillOutput(JsonObject result, TNode scope, SchemaElement item)
    {
        // ADR-0028 invariant: Schema.Add enforces non-empty Field on every
        // child at the construction site. This guard catches the one
        // pathological path left — mutation of a SchemaElement's Field
        // *after* it was added (records here are { get; set; }, not init-only).
        // Pinned for clarity rather than primary defence.
        if (item.Field is null) throw new InvalidOperationException("Schema is invalid");

        if (item is Schema container)
        {
            // Container branch is intentionally NOT guarded: an object-list
            // with no selector throws and aborts the parse, as it always has.
            if (container.IsList)
            {
                result[item.Field] = GetObjectList(scope, container);
            }
            else
            {
                var obj = new JsonObject();

                foreach (var child in container.Children) FillOutput(obj, scope, child);

                result[item.Field] = obj;
            }

            return;
        }

        // ADR-0029: the per-leaf swallow-and-log policy is the documented
        // contract — a malformed value or a backend error on ONE field must
        // not abort the whole page (a web scraper meets noisy pages by
        // default). The catch arms are ordered most-specific first so the
        // log message names the actual failure mode:
        //   - FormatException: int.Parse/float.Parse/bool.Parse/DateTime.Parse
        //     rejected the raw text — typed Coerce failure.
        //   - OverflowException: int.Parse/long.Parse saw a value out of range
        //     for the target type — typed Coerce failure, distinct from format.
        //   - Exception (catch-all third arm): every other defect (backend
        //     selector errors, the rare backend bug) — preserved verbatim
        //     from pre-0029 to keep "a noisy page does not abort the crawl"
        //     load-bearing. Field is left unset in every arm.
        try
        {
            result[item.Field] = item.IsList ? GetValueList(scope, item) : GetSingleValue(scope, item);
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex,
                "Coercion to {Type} failed for field '{Field}' (raw input " +
                "could not be parsed); field left unset",
                item.Type, item.Field);
        }
        catch (OverflowException ex)
        {
            _logger.LogError(ex,
                "Coercion to {Type} overflowed for field '{Field}' (raw " +
                "input out of range for the target type); field left unset",
                item.Type, item.Field);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error extracting field '{Field}' (selector " +
                "'{Selector}'); field left unset",
                item.Field, item.Selector);
        }
    }

    private JsonArray GetObjectList(TNode scope, Schema container)
    {
        var selector = RequireSelector(container);

        var array = new JsonArray();

        foreach (var element in _backend.SelectMany(scope, selector))
        {
            var obj = new JsonObject();

            foreach (var child in container.Children) FillOutput(obj, element, child);

            // Bind the non-generic JsonArray.Add(JsonNode?), not the generic
            // Add<T> (RequiresDynamicCode/UnreferencedCode — AOT-hostile). ADR
            // 0008: the typed fold must trim/AOT-analyse clean.
            array.Add((JsonNode)obj);
        }

        return array;
    }

    private JsonArray GetValueList(TNode scope, SchemaElement item)
    {
        var selector = RequireSelector(item);

        var array = new JsonArray();

        foreach (var node in _backend.SelectMany(scope, selector))
        {
            array.Add(Coerce(item.Type, _backend.ExtractRaw(node, item)));
        }

        return array;
    }

    private JsonNode? GetSingleValue(TNode scope, SchemaElement item)
    {
        var node = _backend.SelectOne(scope, RequireSelector(item));

        if (node is null)
        {
            _logger.LogError(
                "Selector {selector} matched nothing; field {field} will be empty",
                item.Selector, item.Field);

            return JsonValue.Create(string.Empty);
        }

        return Coerce(item.Type, _backend.ExtractRaw(node, item));
    }

    // ADR-0028 invariant: Schema.Add enforces non-empty Selector on every
    // child that needs one (every leaf, every list container). This guard
    // catches mutation-after-Add only — kept as belt-and-suspenders, not
    // primary defence. (A non-list nested Schema is exempt by Add's design
    // and never reaches RequireSelector — the fold's IsList=false container
    // branch nests output without touching the container's own Selector.)
    private static string RequireSelector(SchemaElement item)
    {
        if (string.IsNullOrEmpty(item.Selector))
        {
            throw new InvalidOperationException(
                $"Schema element '{item.Field}' has no selector.");
        }

        return item.Selector;
    }

    // The typed switch was byte-for-byte identical across both old parsers
    // once fed a string, so it is shared grammar here. An untyped leaf is the
    // backend's raw value verbatim. ADR 0008: the JSON backend already bridges
    // its native Newtonsoft token to a JsonNode in JsonSchemaBackend.ExtractRaw
    // (the one place a JToken exists — ADR 0002, backend-local quirk), so this
    // fold carries NO Newtonsoft reference: a JsonNode passes through (JSON
    // keeps number/bool), anything else (an HTML string) is wrapped. That one
    // arm is the whole HTML-vs-JSON divergence (ADR 0002), in S.T.J.Nodes.
    private static JsonNode? Coerce(DataType? type, object? raw) => type switch
    {
        DataType.Integer => JsonValue.Create(int.Parse(raw?.ToString() ?? string.Empty)),
        DataType.Float => JsonValue.Create(float.Parse(raw?.ToString() ?? string.Empty)),
        DataType.Boolean => JsonValue.Create(bool.Parse(raw?.ToString() ?? string.Empty)),
        DataType.DataTime => JsonValue.Create(DateTime.Parse(raw?.ToString() ?? string.Empty)),
        DataType.String => JsonValue.Create(raw?.ToString() ?? string.Empty),
        _ => FromRaw(raw)
    };

    private static JsonNode? FromRaw(object? raw) => raw switch
    {
        null => JsonValue.Create(string.Empty),
        JsonNode n => n,            // JSON backend: already bridged, passthrough
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        double d => JsonValue.Create(d),
        float f => JsonValue.Create(f),
        decimal m => JsonValue.Create(m),
        _ => JsonValue.Create(raw.ToString())
    };
}
