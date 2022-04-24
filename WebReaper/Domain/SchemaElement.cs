namespace WebReaper.Domain;

public class SchemaElement
{
    public SchemaElement[]? Children { get; set; }

    public string Field { get; set; }

    public string? Selector { get; set; }

    public DataType? Type { get; set; }

    public SchemaElement(
        string field,
        string selector,
        DataType type = DataType.String,
        string[]? excludeSelectors = null)
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
        Type = DataType.Array;
    }
}
