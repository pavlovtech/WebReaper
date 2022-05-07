using System.Collections;
using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;
using WebReaper.Domain.Selectors;

namespace WebReaper.Domain.Parsing;

public record Schema(
    string? Field = null,
    string? Selector = null,
    SelectorType SelectorType = SelectorType.Css,
    DataType? Type = null)
    : IEnumerable<Schema>
{
    public List<Schema> Children { get; set; } = new();

    public bool IsComposite => Children.Any();


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