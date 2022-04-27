using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;
using WebReaper.Domain.Schema;
using WebReaper.Domain.Selectors;

namespace WebReaper.Schema;

public abstract record DomSchemaElement(string Field, string Selector, SelectorType SelectorType = SelectorType.Css):
    SchemaElement(Field, Selector, SelectorType){
    protected HtmlNode QuerySelector(HtmlDocument doc, string selector)
    {
        return doc.DocumentNode.QuerySelector(selector);
    }
} 

public record TextSchemaElement(string Field, string Selector, SelectorType SelectorType = SelectorType.Css)
    : DomSchemaElement(Field, Selector, SelectorType)
{
    public override string GetData(HtmlDocument doc)
    {
        var node = QuerySelector(doc, Selector);

        var content = node?.InnerText;

        if(string.IsNullOrWhiteSpace(content)) {
            throw new Exception($"Cannot find element by selector ${Selector}.");
            
        }

        return content;
    }
}

public record ImageSchemaElement(string Field, string Selector, SelectorType SelectorType = SelectorType.Css)
    : DomSchemaElement(Field, Selector, SelectorType)
{
    public override string GetData(HtmlDocument doc)
    {
        var node = QuerySelector(doc, Selector);

        var content = node?.GetAttributeValue("title", "");

        if(string.IsNullOrWhiteSpace(content)) {
            throw new Exception($"Cannot find image link by selector {Selector} in {node?.OuterHtml}.");
        }

        return content;
    }
}

public record UrlSchemaElement(string Field, string Selector, SelectorType SelectorType = SelectorType.Css)
    : DomSchemaElement(Field, Selector, SelectorType)
{
    public override string GetData(HtmlDocument doc)
    {
        var node = QuerySelector(doc, Selector);

        var content = node?.GetAttributeValue("href", "");

        if(string.IsNullOrWhiteSpace(content)) {
            throw new Exception($"No href attribute found by selector {Selector} in {node?.OuterHtml}.");
        }

        return content;
    }
}


public record HtmlSchemaElement(string Field, string Selector, SelectorType SelectorType = SelectorType.Css)
    : DomSchemaElement(Field, Selector, SelectorType)
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