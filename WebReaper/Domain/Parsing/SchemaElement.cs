using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;

namespace WebReaper.Domain.Parsing;

public record SchemaElement(
    string? Field,
    string? Selector = null,
    DataType? Type = null)
{
    public virtual string GetData(HtmlDocument doc)
    {
        var node = doc.DocumentNode.QuerySelector(Selector);

        var content = node?.InnerText;

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException($"Cannot find element by selector ${Selector}.");

        }

        return HtmlEntity.DeEntitize(content);
    }
}
