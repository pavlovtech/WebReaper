namespace WebReaper.Domain.Parsing;

/// <summary>
/// One field in the extraction <see cref="Schema"/>: a named output field, the
/// selector that locates it on the page, and how to read it (an attribute, the
/// inner HTML, a type coercion, or a list). The fold (ADR-0002) walks these;
/// <see cref="Schema"/> is the composite element with <see cref="Schema.Children"/>.
/// </summary>
public record SchemaElement()
{
    /// <summary>An element that extracts text content for
    /// <paramref name="field"/> via <c>Selector</c> set later.
    /// <paramref name="field"/> is nullable to support the root
    /// <see cref="Schema"/> case (Field=null); the property
    /// <see cref="Field"/> matches that nullability.</summary>
    public SchemaElement(string? field) : this()
    {
        Field = field;
    }

    /// <summary>Extract <paramref name="field"/> from the first node matching
    /// <paramref name="selector"/> (its text content).</summary>
    public SchemaElement(string field, string selector) : this(field)
    {
        Selector = selector;
    }

    /// <summary>As <see cref="SchemaElement(string, string)"/>, but coerce the
    /// extracted value to <paramref name="type"/>.</summary>
    public SchemaElement(string field, string selector, DataType type) : this(field, selector)
    {
        Type = type;
    }

    /// <summary>As <see cref="SchemaElement(string, string)"/>, but read the
    /// <paramref name="attr"/> attribute instead of the text content.</summary>
    public SchemaElement(string field, string selector, string attr) : this(field, selector)
    {
        Attr = attr;
    }

    /// <summary>As <see cref="SchemaElement(string, string)"/>, but capture the
    /// node's inner HTML when <paramref name="getHtml"/> is true.</summary>
    public SchemaElement(string field, string selector, bool getHtml) : this(field, selector)
    {
        GetHtml = getHtml;
    }

    /// <summary>The output JSON property name for this element.</summary>
    public string? Field { get; set; }

    /// <summary>The selector (CSS / XPath / JSONPath, per the configured
    /// backend) that locates the value on the page.</summary>
    public string? Selector { get; set; }

    /// <summary>An attribute to read instead of the element's text content
    /// (e.g. <c>href</c>); null reads the text.</summary>
    public string? Attr { get; set; }

    /// <summary>Optional coercion applied to the extracted value (see
    /// <see cref="DataType"/>); null keeps the raw string.</summary>
    public DataType? Type { get; set; }

    /// <summary>Capture the matched node's inner HTML rather than its text.</summary>
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
