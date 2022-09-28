using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;

namespace WebReaper.DOM;

public record Dynamic(
    string Field,
    string Selector,
    Func<HtmlNode, string> Transform,
    SelectorType? SelectorType = SelectorType.Css)
    : SchemaElement(Field, Selector, SelectorType)
{
    public override string GetData(HtmlDocument doc) =>
        Transform(doc.DocumentNode.QuerySelector(Selector));
}
