using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;
using WebReaper.Domain.Parsing;

namespace WebReaper.DOM;

public record Text(string Field, string Selector)
    : SchemaElement(Field, Selector)
{
    public override string GetData(HtmlDocument doc)
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
