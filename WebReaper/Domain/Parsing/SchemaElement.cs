using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;

namespace WebReaper.Domain.Parsing;

public record SchemaElement(
    string? Field,
    string? Selector = null,
    string? Attr = null,
    DataType? Type = null)
{
    public virtual string GetData(HtmlDocument doc)
    {
        var node = doc.DocumentNode.QuerySelector(Selector);

        if (node is null)
        {
            throw new InvalidOperationException($"Cannot find element by selector ${Selector}.");
        }

        string? content = null;

        if (Attr is not null)
        {
            if (Attr is not "src")
            {
                content = node?.GetAttributeValue(Attr, "");
            }
            else
            {
                content = node?.GetAttributeValue(Attr, "title"); // HTML Agility Pack workaround
            }
        }
        else
        {
            content = node?.InnerText;
        }

        return HtmlEntity.DeEntitize(content);
    }
}
