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
    : Schema(Field, null, SelectorType, null), ICollection<Schema>
{
    public SchemaContainer():this(null, SelectorType.Css)
    {
    }

    public List<Schema> Children { get; set; } = new();

    public int Count => Children.Count;

    public bool IsReadOnly => false;

    public virtual void Add(Schema element) => Children.Add(element);

    public void Clear()
    {
        Children.Clear();
    }

    public bool Contains(Schema item)
    {
        return Children.Contains(item);
    }

    public void CopyTo(Schema[] array, int arrayIndex)
    {
        Children.ToArray().CopyTo(array, arrayIndex);
    }

    public IEnumerator<Schema> GetEnumerator() => Children.GetEnumerator();

    public bool Remove(Schema item)
    {
        return Children.Remove(item);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}