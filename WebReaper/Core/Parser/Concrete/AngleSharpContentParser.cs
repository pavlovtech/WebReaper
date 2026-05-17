using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Concrete;

/// <summary>
/// HTML content parser: the shared <see cref="SchemaContentParser{TNode}"/>
/// fold over the <see cref="AngleSharpSchemaBackend"/>. Kept as a named
/// type with its original <c>(ILogger)</c> constructor so it stays the
/// default and a drop-in for existing callers.
/// </summary>
public class AngleSharpContentParser : IJsonContentParser
{
    private readonly SchemaContentParser<AngleSharp.Dom.IParentNode> _inner;

    public AngleSharpContentParser(ILogger logger)
        => _inner = new SchemaContentParser<AngleSharp.Dom.IParentNode>(new AngleSharpSchemaBackend(), logger);

    public Task<JsonObject> ParseToJsonAsync(string html, Schema? schema)
        => _inner.ParseToJsonAsync(html, schema);
}
