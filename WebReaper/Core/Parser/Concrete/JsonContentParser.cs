using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Concrete;

/// <summary>
/// Parses a JSON response instead of HTML (issue #27 — scraping JSON
/// endpoints such as the WordPress REST API). The shared
/// <see cref="SchemaContentParser{TNode}"/> fold over the
/// <see cref="JsonSchemaBackend"/>: each <see cref="SchemaElement.Selector"/>
/// is a JSONPath expression and <see cref="SchemaElement.IsList"/> works
/// exactly as it does for the HTML parser. Kept as a named type with its
/// original <c>(ILogger)</c> constructor so <c>WithJsonContentParser</c>
/// and existing callers are unaffected.
/// <para>
/// The <c>JToken</c> type argument is the JSON backend's Newtonsoft JSONPath
/// scope cursor — System.Text.Json has no JSONPath — and is the named ADR-0008
/// follow-up that gates a zero-warning whole-core <c>PublishAot</c>; it is not
/// the removed JObject shim.
/// </para>
/// </summary>
public class JsonContentParser : IJsonContentParser
{
    private readonly SchemaContentParser<JToken> _inner;

    public JsonContentParser(ILogger logger)
        => _inner = new SchemaContentParser<JToken>(new JsonSchemaBackend(), logger);

    public Task<JsonObject> ParseToJsonAsync(string json, Schema? schema)
        => _inner.ParseToJsonAsync(json, schema);
}
