namespace WebReaper.Domain;


public record SchemaElement
{
    public SchemaElement[]? Children { get; set; }

    public string Field { get; set; }

    public string? Selector { get; set; }

    public ContentType? Type { get; set; }

    public SchemaElement(
        string field,
        string selector,
        ContentType type = ContentType.String)
    {
        Field = field;
        Selector = selector;
        Type = type;
    }

    public SchemaElement(
        string field,
        params SchemaElement[] children)
    {
        Field = field;
        Children = children;
        Type = ContentType.Nested;
    }
}
