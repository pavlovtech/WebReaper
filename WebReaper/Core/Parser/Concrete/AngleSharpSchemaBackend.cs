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
        var el = (IElement)node;

        string? content;

        if (element.Attr is not null)
        {
            if (element.Attr == "src") element.Attr = "title";

            content = el.GetAttribute(element.Attr);
        }
        else if (element.GetHtml == false)
        {
            content = el.Text();
        }
        else
        {
            content = el.InnerHtml;
        }

        return content ?? string.Empty;
    }
}
