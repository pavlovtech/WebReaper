using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
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
/// ADR 0008 named follow-up, now closed: the scope cursor is a
/// <see cref="JsonNode"/> queried by <see cref="JsonSchemaBackend"/>'s in-repo
/// JSONPath-subset evaluator. Core no longer reaches Newtonsoft on the JSON
/// path; the only remaining core Newtonsoft reach is the separate
/// <c>CookieStore</c> payload-shell sibling.
/// </para>
/// </summary>
public class JsonContentParser : IJsonContentParser
{
    private readonly SchemaContentParser<JsonNode> _inner;

    public JsonContentParser(ILogger logger)
        => _inner = new SchemaContentParser<JsonNode>(new JsonSchemaBackend(), logger);

    public Task<JsonObject> ParseToJsonAsync(string json, Schema? schema)
        => _inner.ParseToJsonAsync(json, schema);
}
