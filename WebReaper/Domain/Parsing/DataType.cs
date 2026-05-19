namespace WebReaper.Domain.Parsing;

/// <summary>
/// The type a <see cref="SchemaElement"/>'s extracted text is coerced to in
/// the output JSON. <see cref="None"/> (the default) keeps the raw string.
/// </summary>
public enum DataType
{
    /// <summary>No coercion — keep the extracted string verbatim.</summary>
    None = 0,

    /// <summary>Parse the extracted text as an integer.</summary>
    Integer = 1,

    /// <summary>Parse the extracted text as a floating-point number.</summary>
    Float = 2,

    /// <summary>Parse the extracted text as a boolean.</summary>
    Boolean = 3,

    /// <summary>Keep the value as a string (explicit).</summary>
    String = 4,

    /// <summary>Parse the extracted text as a date/time.</summary>
    DataTime = 5,

    /// <summary>Treat the element as a nested object — its child selectors
    /// produce a sub-object rather than a scalar.</summary>
    Object = 6
}
