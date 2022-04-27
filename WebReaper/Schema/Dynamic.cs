using HtmlAgilityPack;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;

namespace WebReaper.Parsing;

public record Dynamic(
    string Field,
    string Selector, 
    Func<HtmlNode, string> Transform,
    SelectorType SelectorType = SelectorType.Css)
    : Schema(Field, Selector, SelectorType)
{
    public override string GetData(HtmlDocument doc) =>
        Transform(QuerySelector(doc, Selector));
}
