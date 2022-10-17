using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;

namespace WebReaper.Domain.Parsing;

public record Dynamic(
    string Field,
    string Selector,
    Func<HtmlNode, string> Transform)
    : SchemaElement(Field, Selector)
{
    public override string GetData(HtmlDocument doc) =>
        Transform(doc.DocumentNode.QuerySelector(Selector));
}
