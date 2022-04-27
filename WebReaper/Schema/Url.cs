using HtmlAgilityPack;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;

namespace WebReaper.Parsing;

public record Url(string Field, string Selector, SelectorType SelectorType = SelectorType.Css)
    : Schema(Field, Selector, SelectorType)
{
    public override string GetData(HtmlDocument doc)
    {
        var node = QuerySelector(doc, Selector!);

        var content = node?.GetAttributeValue("href", "");

        if(string.IsNullOrWhiteSpace(content)) {
            throw new Exception($"No href attribute found by selector {Selector} in {node?.OuterHtml}.");
        }

        return content;
    }
}
