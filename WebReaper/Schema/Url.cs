using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;

namespace WebReaper.Core.Schema;

public record Url(string Field, string Selector, SelectorType? SelectorType = SelectorType.Css)
    : SchemaElement(Field, Selector, SelectorType)
{
    public override string GetData(HtmlDocument doc)
    {
        var node = doc.DocumentNode.QuerySelector(Selector);

        var content = node?.GetAttributeValue("href", "");

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException($"No href attribute found by selector {Selector} in {node?.OuterHtml}.");
        }

        return HtmlEntity.DeEntitize(content);
    }
}
