
using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;

namespace WebReaper.Parsing;

public record Image(string Field, string Selector, SelectorType? SelectorType = SelectorType.Css)
    : SchemaElement(Field, Selector, SelectorType)
{
    public override string GetData(HtmlDocument doc)
    {
        var node = doc.DocumentNode.QuerySelector(Selector);

        var content = node?.GetAttributeValue("title", "");

        if(string.IsNullOrWhiteSpace(content)) {
            throw new InvalidOperationException($"Cannot find image link by selector {Selector} in {node?.OuterHtml}.");
        }

        return HtmlEntity.DeEntitize(content);
    }
}
