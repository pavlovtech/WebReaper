using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;
using WebReaper.Domain.Parsing;

namespace WebReaper.DOM;

public record Dynamic(
    string Field,
    string Selector,
    Func<HtmlNode, string> Transform)
    : SchemaElement(Field, Selector)
{
    public override string GetData(HtmlDocument doc) =>
        Transform(doc.DocumentNode.QuerySelector(Selector));
}
