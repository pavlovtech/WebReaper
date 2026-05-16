using Newtonsoft.Json.Linq;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Concrete;

/// <summary>
/// JSON backend for <see cref="SchemaContentParser{TNode}"/> (issue #27 —
/// scraping JSON endpoints such as the WordPress REST API). The scope
/// cursor is a <see cref="JToken"/> and each
/// <see cref="SchemaElement.Selector"/> is a JSONPath expression.
/// <see cref="ExtractRaw"/> returns the native token, so an untyped leaf
/// keeps its JSON type (a number stays a number) — the structured-side
/// of the divergence the shared fold passes straight through.
/// </summary>
internal sealed class JsonSchemaBackend : ISchemaBackend<JToken>
{
    public Task<JToken> RootAsync(string content) => Task.FromResult(JToken.Parse(content));

    public IEnumerable<JToken> SelectMany(JToken scope, string selector)
        => scope.SelectTokens(selector);

    public JToken? SelectOne(JToken scope, string selector)
        => scope.SelectToken(selector);

    // JSON has no attribute / markup notion; the native token is the raw
    // value (DeepClone so the result is detached from the parsed tree).
    public object? ExtractRaw(JToken node, SchemaElement element) => node.DeepClone();
}
