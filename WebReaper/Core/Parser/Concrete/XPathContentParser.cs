using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Concrete;

/// <summary>
/// Parses an HTML response with XPath selectors instead of CSS (discussion
/// #17). The shared <see cref="SchemaContentParser{TNode}"/> fold over the
/// <see cref="AngleSharpXPathSchemaBackend"/>: each
/// <see cref="SchemaElement.Selector"/> is an XPath 1.0 expression and
/// <see cref="SchemaElement.IsList"/> works exactly as it does for the CSS
/// and JSON parsers. Kept as a named type with an <c>(ILogger)</c>
/// constructor so <c>WithXPathContentParser</c> mirrors
/// <c>WithJsonContentParser</c>.
/// </summary>
public class XPathContentParser : IContentParser
{
    private readonly SchemaContentParser<AngleSharp.Dom.IParentNode> _inner;

    public XPathContentParser(ILogger logger)
        => _inner = new SchemaContentParser<AngleSharp.Dom.IParentNode>(new AngleSharpXPathSchemaBackend(), logger);

    public Task<JObject> ParseAsync(string content, Schema? schema) => _inner.ParseAsync(content, schema);
}
