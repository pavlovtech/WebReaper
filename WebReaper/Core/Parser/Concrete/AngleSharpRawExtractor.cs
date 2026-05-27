using AngleSharp.Dom;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Concrete;

/// <summary>
/// The AngleSharp-DOM markup-leaf grammar (ADR-0027): one home for the
/// three-arm dispatch every AngleSharp <see cref="Abstract.ISchemaBackend{TNode}"/>
/// shares — attribute extraction (when <see cref="SchemaElement.Attr"/>
/// is set), inner-HTML (when <see cref="SchemaElement.GetHtml"/> is
/// true), or text content (otherwise). The default is the AngleSharp
/// <c>IElement</c>'s own <see cref="IElement.GetAttribute(string)"/> /
/// <see cref="IElement.InnerHtml"/> / <c>IElement.Text()</c>; a
/// missing attribute returns <see cref="string.Empty"/>, never null.
/// </summary>
/// <remarks>
/// The helper is family-internal — only the AngleSharp CSS and XPath
/// backends share this grammar. The JSON backend has a different leaf
/// grammar (a deep-cloned <see cref="System.Text.Json.Nodes.JsonNode"/>,
/// per ADR-0002's untyped-leaf raw-value pin) and does not call this.
/// Backend-local <em>quirks</em> (e.g. the CSS backend's <c>src</c>→<c>title</c>
/// rewrite, ADR-0007) stay in the calling backend and are applied
/// <em>before</em> delegation; the helper itself is quirk-free.
/// </remarks>
internal static class AngleSharpRawExtractor
{
    public static string ExtractRaw(IElement element, SchemaElement schemaElement)
    {
        string? content;

        if (schemaElement.Attr is not null)
            content = element.GetAttribute(schemaElement.Attr);
        else if (schemaElement.GetHtml == false)
            content = element.Text();
        else
            content = element.InnerHtml;

        return content ?? string.Empty;
    }
}
