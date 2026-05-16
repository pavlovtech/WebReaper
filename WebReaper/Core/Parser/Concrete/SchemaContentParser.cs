using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Concrete;

/// <summary>
/// The single recursive <see cref="Schema"/> fold, shared by every
/// backend. It owns the grammar the parsers used to each re-implement:
/// the container / object-list / leaf / value-list branching, the
/// <see cref="SchemaElement.Type"/> coercion, the missing-node →
/// log → empty-string policy, and the swallow-and-log scope (only a
/// leaf assignment is guarded; a container whose list selector is
/// missing still throws, exactly as before). Everything document-shaped
/// is delegated to <see cref="ISchemaBackend{TNode}"/>.
/// </summary>
/// <remarks>
/// Public and generic on purpose: a new backend is
/// <c>new SchemaContentParser&lt;TNode&gt;(myBackend, logger)</c> passed
/// to <c>WithContentParser</c>, reusing this proven fold rather than
/// copying it. See docs/adr/0002.
/// </remarks>
public class SchemaContentParser<TNode> : IContentParser where TNode : class
{
    private readonly ISchemaBackend<TNode> _backend;
    private readonly ILogger _logger;

    public SchemaContentParser(ISchemaBackend<TNode> backend, ILogger logger)
    {
        _backend = backend;
        _logger = logger;
    }

    public async Task<JObject> ParseAsync(string content, Schema? schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var root = await _backend.RootAsync(content);

        try
        {
            var output = new JObject();

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
    private void FillOutput(JObject result, TNode scope, SchemaElement item)
    {
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
                var obj = new JObject();

                foreach (var child in container.Children) FillOutput(obj, scope, child);

                result[item.Field] = obj;
            }

            return;
        }

        try
        {
            result[item.Field] = item.IsList ? GetValueList(scope, item) : GetSingleValue(scope, item);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during parsing phase");
        }
    }

    private JArray GetObjectList(TNode scope, Schema container)
    {
        var selector = RequireSelector(container);

        var array = new JArray();

        foreach (var element in _backend.SelectMany(scope, selector))
        {
            var obj = new JObject();

            foreach (var child in container.Children) FillOutput(obj, element, child);

            array.Add(obj);
        }

        return array;
    }

    private JArray GetValueList(TNode scope, SchemaElement item)
    {
        var selector = RequireSelector(item);

        var array = new JArray();

        foreach (var node in _backend.SelectMany(scope, selector))
        {
            array.Add(Coerce(item.Type, _backend.ExtractRaw(node, item)));
        }

        return array;
    }

    private JToken GetSingleValue(TNode scope, SchemaElement item)
    {
        var node = _backend.SelectOne(scope, RequireSelector(item));

        if (node is null)
        {
            _logger.LogError(
                "Selector {selector} matched nothing; field {field} will be empty",
                item.Selector, item.Field);

            return string.Empty;
        }

        return Coerce(item.Type, _backend.ExtractRaw(node, item));
    }

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
    // once fed a string, so it is shared grammar here. An untyped leaf is
    // the backend's raw value verbatim: a native JToken passes through
    // (JSON keeps number/bool), anything else is wrapped (HTML stays a
    // string). That one line is the whole HTML-vs-JSON divergence.
    private static JToken Coerce(DataType? type, object? raw) => type switch
    {
        DataType.Integer => int.Parse(raw?.ToString() ?? string.Empty),
        DataType.Float => float.Parse(raw?.ToString() ?? string.Empty),
        DataType.Boolean => bool.Parse(raw?.ToString() ?? string.Empty),
        DataType.DataTime => DateTime.Parse(raw?.ToString() ?? string.Empty),
        DataType.String => raw?.ToString() ?? string.Empty,
        _ => raw is JToken token ? token : JToken.FromObject(raw ?? string.Empty)
    };
}
