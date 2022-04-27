namespace WebReaper.Domain.Schema;
using HtmlAgilityPack;
using WebReaper.Domain.Selectors;
using Fizzler.Systems.HtmlAgilityPack;

public record SchemaElement(string Field, string? Selector = null, SelectorType SelectorType = SelectorType.Css) {
    protected HtmlNode QuerySelector(HtmlDocument doc, string selector)
    {
        return doc.DocumentNode.QuerySelector(selector);
    }

    public virtual string GetData(HtmlDocument doc)
    {
        var node = QuerySelector(doc, Selector);

        var content = node?.InnerText;

        if(string.IsNullOrWhiteSpace(content)) {
            throw new Exception($"Cannot find element by selector ${Selector}.");
            
        }

        return content;
    }
};
