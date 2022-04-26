using WebReaper.Domain.Schema;
using WebReaper.Domain.Selectors;

public record SchemaElement
{
    public SchemaElement[]? Children { get; set; }

    public string Field { get; set; }

    public string? Selector { get; set; }

    public SelectorType SelectorType { get; init; } = SelectorType.Css;

    public ContentType? ContentType { get; init; } = WebReaper.Domain.Schema.ContentType.String;

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
        ContentType = WebReaper.Domain.Schema.ContentType.Nested;
    }
}
