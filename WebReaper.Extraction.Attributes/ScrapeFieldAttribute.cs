namespace WebReaper.Extraction.Attributes;

/// <summary>
/// Marks a property as a <c>Schema</c> leaf to extract. Consumed by the
/// <c>WebReaper.Extraction.Generators</c> Roslyn source generator
/// (ADR-0045).
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ScrapeFieldAttribute : Attribute
{
    /// <summary>Construct the attribute with a required selector.</summary>
    /// <param name="selector">The CSS / XPath / JSONPath selector — passes
    /// to the fold's backend unchanged.</param>
    public ScrapeFieldAttribute(string selector)
    {
        Selector = selector;
    }

    /// <summary>The CSS / XPath / JSONPath selector.</summary>
    public string Selector { get; }

    /// <summary>The data type. <see cref="SchemaFieldType.Auto"/> (the
    /// default) infers from the property's CLR type — <c>int</c> →
    /// <c>Integer</c>, <c>string</c> → <c>String</c>, etc. Set
    /// explicitly when the CLR type cannot be inferred.</summary>
    public SchemaFieldType Type { get; set; } = SchemaFieldType.Auto;

    /// <summary>If <c>true</c>, the selector matches a list of values;
    /// the property must be a <c>List&lt;T&gt;</c> or <c>T[]</c>.
    /// Default <c>false</c>.</summary>
    public bool IsList { get; set; }

    /// <summary>Override the HTML attribute the value is read from
    /// (e.g. <c>"href"</c> for links, <c>"datetime"</c> for time
    /// tags). Defaults to <c>null</c> — the backend's default
    /// (text content for HTML, value for JSON).</summary>
    public string? Attr { get; set; }
}
