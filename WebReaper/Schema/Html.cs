using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;

namespace WebReaper.Parsing;

public record Html(string Field, string Selector, SelectorType SelectorType = SelectorType.Css)
    : Schema(Field, Selector, SelectorType)
{
    public override string GetData(HtmlDocument doc)
    {
        var node = doc.DocumentNode.QuerySelector(Selector);

        var content = node?.InnerHtml;

        if(string.IsNullOrWhiteSpace(content)) {
            throw new InvalidOperationException($"No html found by selector {Selector} in {node?.OuterHtml}.");
        }

        return content;
    }
}
