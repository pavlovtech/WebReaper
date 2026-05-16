using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.XPath;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Concrete;

/// <summary>
/// XPath backend for <see cref="SchemaContentParser{TNode}"/>: the same
/// AngleSharp DOM as <see cref="AngleSharpSchemaBackend"/>, but each
/// <see cref="SchemaElement.Selector"/> is an XPath 1.0 expression instead
/// of a CSS selector (discussion #17, realising the ADR 0002 seam — a new
/// selector language is an <see cref="ISchemaBackend{TNode}"/>, not a
/// re-derivation of the fold). The scope cursor is <see cref="IParentNode"/>
/// exactly as for the CSS backend: the <see cref="IDocument"/> at the top
/// (so the fold's <c>IDisposable</c> dispose still runs), a matched
/// <see cref="IElement"/> when recursing into a list of objects — so a
/// relative XPath (<c>.//span</c>) resolves against each match.
///
/// Unlike <see cref="AngleSharpSchemaBackend"/> this backend does NOT carry
/// the legacy <c>src</c>→<c>title</c> attribute rewrite: that is a
/// quarantined quirk of the CSS backend (ADR 0002 — quirks are backend-local
/// and a new backend is not obliged to copy another's), not part of the seam
/// contract. XPath attribute extraction returns the attribute asked for.
/// </summary>
internal sealed class AngleSharpXPathSchemaBackend : ISchemaBackend<IParentNode>
{
    public async Task<IParentNode> RootAsync(string content)
    {
        var config = Configuration.Default.WithDefaultLoader();
        var context = BrowsingContext.New(config);

        return await context.OpenAsync(resp =>
            resp.Header("Content-Type", "text/html; charset=utf-8").Content(content));
    }

    public IEnumerable<IParentNode> SelectMany(IParentNode scope, string selector)
    {
        var context = ContextElement(scope);
        if (context is null) return Enumerable.Empty<IParentNode>();

        return context.SelectNodes(selector).OfType<IElement>();
    }

    public IParentNode? SelectOne(IParentNode scope, string selector)
        => ContextElement(scope)?.SelectSingleNode(selector) as IElement;

    public object? ExtractRaw(IParentNode node, SchemaElement element)
    {
        var el = (IElement)node;

        string? content;

        if (element.Attr is not null)
            content = el.GetAttribute(element.Attr);
        else if (element.GetHtml == false)
            content = el.Text();
        else
            content = el.InnerHtml;

        return content ?? string.Empty;
    }

    // IParentNode is the IDocument at the top level and a matched IElement
    // when recursing; AngleSharp.XPath's extensions hang off IElement, so the
    // document maps to its root element.
    private static IElement? ContextElement(IParentNode scope)
        => scope as IElement ?? (scope as IDocument)?.DocumentElement;
}
