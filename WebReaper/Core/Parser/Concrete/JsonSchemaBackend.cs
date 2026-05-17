using System.Text.Json.Nodes;
using Newtonsoft.Json.Linq;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Concrete;

/// <summary>
/// JSON backend for <see cref="SchemaContentParser{TNode}"/> (issue #27 —
/// scraping JSON endpoints such as the WordPress REST API). The scope
/// cursor is a Newtonsoft <see cref="JToken"/> and each
/// <see cref="SchemaElement.Selector"/> is a JSONPath expression
/// (System.Text.Json has no JSONPath — ADR 0008 Bounded scope; this cursor
/// is the named follow-up gating a zero-warning whole-core PublishAot).
/// <para>
/// ADR 0008: <see cref="ExtractRaw"/> returns the native value already
/// bridged to a <see cref="System.Text.Json.Nodes.JsonNode"/>. The
/// Newtonsoft→STJ bridge lives HERE, in the one backend that has a
/// Newtonsoft token, not in the shared fold — so the fold/typed terminal
/// carry no Newtonsoft reference (ADR 0002: a backend's document quirk is
/// backend-local). An untyped leaf still keeps its JSON type (a number
/// stays a number) — the structured side of the ADR-0002 divergence,
/// preserved through the bridge.
/// </para>
/// </summary>
internal sealed class JsonSchemaBackend : ISchemaBackend<JToken>
{
    public Task<JToken> RootAsync(string content) => Task.FromResult(JToken.Parse(content));

    public IEnumerable<JToken> SelectMany(JToken scope, string selector)
        => scope.SelectTokens(selector);

    public JToken? SelectOne(JToken scope, string selector)
        => scope.SelectToken(selector);

    // The native token bridged to a JsonNode (detached from the parsed tree).
    // Reflection-free: switch on JTokenType, never JToken.ToObject/dynamic.
    public object? ExtractRaw(JToken node, SchemaElement element) => ToNode(node);

    private static JsonNode? ToNode(JToken t) => t.Type switch
    {
        JTokenType.Integer => JsonValue.Create((long)t),
        JTokenType.Float => JsonValue.Create((double)t),
        JTokenType.Boolean => JsonValue.Create((bool)t),
        JTokenType.Null => null,
        JTokenType.Array => ArrayFrom((JArray)t),
        JTokenType.Object => ObjectFrom((JObject)t),
        _ => JsonValue.Create((string?)t ?? string.Empty)
    };

    private static JsonArray ArrayFrom(JArray a)
    {
        var arr = new JsonArray();
        foreach (var e in a) arr.Add(ToNode(e));
        return arr;
    }

    private static JsonObject ObjectFrom(JObject o)
    {
        var obj = new JsonObject();
        foreach (var p in o) obj[p.Key] = p.Value is null ? null : ToNode(p.Value);
        return obj;
    }
}
