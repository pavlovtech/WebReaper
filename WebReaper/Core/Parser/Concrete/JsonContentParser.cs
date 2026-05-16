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
/// </summary>
public class JsonContentParser : IContentParser
{
    private readonly SchemaContentParser<JToken> _inner;

    public JsonContentParser(ILogger logger)
        => _inner = new SchemaContentParser<JToken>(new JsonSchemaBackend(), logger);

    public Task<JObject> ParseAsync(string json, Schema? schema) => _inner.ParseAsync(json, schema);
}
