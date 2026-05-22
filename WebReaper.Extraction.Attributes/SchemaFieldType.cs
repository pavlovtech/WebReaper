namespace WebReaper.Extraction.Attributes;

/// <summary>
/// The data types a <see cref="ScrapeFieldAttribute"/> can declare —
/// mirror of <c>WebReaper.Domain.Parsing.DataType</c>. Kept in this
/// attributes package so consumer code does not import the WebReaper
/// domain namespace.
/// </summary>
public enum SchemaFieldType
{
    /// <summary>Inferred from the property's CLR type at generation
    /// time.</summary>
    Auto = 0,

    /// <summary>String — the default for typed leaves.</summary>
    String,

    /// <summary>Integer (mapped to <c>int</c> by the fold's
    /// Coerce).</summary>
    Integer,

    /// <summary>Floating-point.</summary>
    Float,

    /// <summary>Boolean.</summary>
    Boolean,

    /// <summary>DateTime — the model returns an ISO 8601 string; the
    /// fold's Coerce calls <c>DateTime.Parse</c>.</summary>
    DateTime,
}
