using System.Collections;
using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;
using WebReaper.Domain.Selectors;

namespace WebReaper.Domain.Parsing;

public record Schema(
    string? Field,
    string? Selector = null,
    SelectorType SelectorType = SelectorType.Css,
    DataType? Type = null)
{
    public virtual string GetData(HtmlDocument doc)
    {
        var node = doc.DocumentNode.QuerySelector(Selector);

        var content = node?.InnerText;

        if(string.IsNullOrWhiteSpace(content)) {
            throw new InvalidOperationException($"Cannot find element by selector ${Selector}.");
            
        }

        return content;
    }
}

public record SchemaContainer(
    string? Field = null,
    SelectorType SelectorType = SelectorType.Css)
    : Schema(Field, null, SelectorType, null), IEnumerable<Schema>
{
    public List<Schema> Children { get; set; } = new();

    public virtual void Add(Schema element) => Children.Add(element);

    public virtual string GetData(HtmlDocument doc)
    {
        var node = doc.DocumentNode.QuerySelector(Selector);

        var content = node?.InnerText;

        if(string.IsNullOrWhiteSpace(content)) {
            throw new InvalidOperationException($"Cannot find element by selector ${Selector}.");
            
        }

        return content;
    }

    public IEnumerator<Schema> GetEnumerator() => Children.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}