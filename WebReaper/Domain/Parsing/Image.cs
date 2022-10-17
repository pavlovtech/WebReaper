
using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;

namespace WebReaper.Domain.Parsing;

public record Image(string Field, string Selector)
    : SchemaElement(Field, Selector)
{
    public override string GetData(HtmlDocument doc)
    {
        var node = doc.DocumentNode.QuerySelector(Selector);

        var content = node?.GetAttributeValue("title", "");

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException($"Cannot find image link by selector {Selector} in {node?.OuterHtml}.");
        }

        return HtmlEntity.DeEntitize(content);
    }
}
