using AngleSharp;
using AngleSharp.Dom;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Concrete;

/// <summary>
/// HTML backend for <see cref="SchemaContentParser{TNode}"/>: AngleSharp
/// DOM, CSS selectors. The scope cursor is <see cref="IParentNode"/>
/// (the document at the top, a matched element when recursing). This is
/// the ONLY place the <c>src</c>→<c>title</c> rewrite and the
/// attr/markup/text rules live — the shared fold never sees them.
/// </summary>
internal sealed class AngleSharpSchemaBackend : ISchemaBackend<IParentNode>
{
    public async Task<IParentNode> RootAsync(string content)
    {
        var config = Configuration.Default.WithDefaultLoader();
        var context = BrowsingContext.New(config);

        // TODO temp fix
        return await context.OpenAsync(resp =>
            resp.Header("Content-Type", "text/html; charset=utf-8").Content(content));
    }

    public IEnumerable<IParentNode> SelectMany(IParentNode scope, string selector)
        => scope.QuerySelectorAll(selector);

    public IParentNode? SelectOne(IParentNode scope, string selector)
        => scope.QuerySelector(selector);

    public object? ExtractRaw(IParentNode node, SchemaElement element)
    {
        // ADR-0007: this backend's quarantined legacy quirk — a requested
        // src attribute is silently rewritten to title (the XPath backend
        // deliberately does not copy it; pinned by SchemaFoldTests'
        // Src_to_title_rewrite_is_quarantined_in_the_html_backend, which
        // also asserts the in-place mutation on the SchemaElement).
        // Quirk first, shared AngleSharp-DOM grammar second (ADR-0027).
        if (element.Attr == "src") element.Attr = "title";
        return AngleSharpRawExtractor.ExtractRaw((IElement)node, element);
    }
}
