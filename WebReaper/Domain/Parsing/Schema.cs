using System.Collections;
using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;

namespace WebReaper.Domain.Parsing;

public record SchemaElement(
    string? Field,
    string? Selector = null,
    DataType? Type = null)
{
    public virtual string GetData(HtmlDocument doc)
    {
        var node = doc.DocumentNode.QuerySelector(Selector);

        var content = node?.InnerText;

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException($"Cannot find element by selector ${Selector}.");

        }

        return HtmlEntity.DeEntitize(content);
    }
}

public record Schema(string? Field = null)
    : SchemaElement(Field), ICollection<SchemaElement>
{
    public Schema() : this(Field: null)
    {

    }

    public List<SchemaElement> Children { get; set; } = new();

    public int Count => Children.Count;

    public bool IsReadOnly => false;

    public virtual void Add(SchemaElement element) => Children.Add(element);

    public void Clear()
    {
        Children.Clear();
    }

    public bool Contains(SchemaElement item)
    {
        return Children.Contains(item);
    }

    public void CopyTo(SchemaElement[] array, int arrayIndex)
    {
        Children.ToArray().CopyTo(array, arrayIndex);
    }

    public IEnumerator<SchemaElement> GetEnumerator() => Children.GetEnumerator();

    public bool Remove(SchemaElement item)
    {
        return Children.Remove(item);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}