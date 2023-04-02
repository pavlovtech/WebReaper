namespace WebReaper.Domain.Parsing;

public record SchemaElement()
{
    public SchemaElement(string field) : this()
    {
        Field = field;
    }

    public SchemaElement(string field, string selector) : this(field)
    {
        Selector = selector;
    }

    public SchemaElement(string field, string selector, DataType type) : this(field, selector)
    {
        Type = type;
    }

    public SchemaElement(string field, string selector, string attr) : this(field, selector)
    {
        Attr = attr;
    }

    public SchemaElement(string field, string selector, bool getHtml) : this(field, selector)
    {
        GetHtml = getHtml;
    }

    public string? Field { get; set; }
    public string? Selector { get; set; }
    public string? Attr { get; set; }
    public DataType? Type { get; set; }
    public bool GetHtml { get; set; }
}