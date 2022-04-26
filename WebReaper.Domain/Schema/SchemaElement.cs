using WebReaper.Domain.Schema;
using WebReaper.Domain.Selectors;

public record SchemaElement
{
    public SchemaElement[]? Children { get; set; }

    public string Field { get; set; }

    public string? Selector { get; set; }
    public Func<string, string> Transform { get; init; } = p => p;
    public SelectorType SelectorType { get; init; } = SelectorType.Css;

    public ElementType? ElementType { get; init; } = WebReaper.Domain.Schema.ElementType.Text;

    public SchemaElement(
        string field,
        string selector,
        Func<string, string> transform):
    this(field, selector)
    {
        Transform = transform;
    }

    public SchemaElement(
        string field,
        string selector)
    {
        Field = field;
        Selector = selector;
    }

    public SchemaElement(
        string field,
        params SchemaElement[] children)
    {
        Field = field;
        Children = children;
        ElementType = WebReaper.Domain.Schema.ElementType.Nested;
    }
}
