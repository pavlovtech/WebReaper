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

    /// <summary>
    /// When true, every node matching <see cref="Selector"/> is
    /// extracted into a JSON array instead of just the first match.
    /// For a <see cref="Schema"/> element this yields an array of
    /// objects (child selectors evaluated relative to each match);
    /// for a leaf element it yields an array of values. Issue #28.
    /// </summary>
    public bool IsList { get; set; }
}