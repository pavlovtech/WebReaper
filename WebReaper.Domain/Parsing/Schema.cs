using System.Collections;
using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;
using WebReaper.Domain.Selectors;

namespace WebReaper.Domain.Parsing;

public record Schema(
    string? Field = null,
    string? Selector = null,
    SelectorType SelectorType = SelectorType.Css)
    : IEnumerable<Schema>
{
    protected List<Schema> Children = new List<Schema>();

    public bool IsComposite => Children.Any();


    public virtual void Add(Schema element) {
        Children.Add(element);
    }

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

    public IEnumerator<Schema> GetEnumerator()
    {
        foreach(var child in Children) {
            yield return child;
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}