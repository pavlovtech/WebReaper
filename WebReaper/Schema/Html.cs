using HtmlAgilityPack;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;

namespace WebReaper.Parsing;

public record Html(string Field, string Selector, SelectorType SelectorType = SelectorType.Css)
    : Schema(Field, Selector, SelectorType)
{
    public override string GetData(HtmlDocument doc)
    {
        var node = QuerySelector(doc, Selector);

        var content = node?.InnerHtml;

        if(string.IsNullOrWhiteSpace(content)) {
            throw new Exception($"No html found in convert {content}.");
        }

        return content;
    }
}
