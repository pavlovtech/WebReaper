namespace WebReaper.Extraction.Attributes;

/// <summary>
/// Marks a class as a <c>Schema</c> source. The
/// <c>WebReaper.Extraction.Generators</c> Roslyn source generator
/// (ADR-0045) emits a <c>public static Schema Schema { get; }</c> and
/// a <c>public static T Materialize(JsonObject)</c> on the class.
/// <para>
/// The class must be declared <c>partial</c> so the generator can add
/// to it. Properties to extract are marked with
/// <see cref="ScrapeFieldAttribute"/>; properties without it are
/// ignored.
/// </para>
/// </summary>
/// <example>
/// <code>
/// [ScrapeSchema]
/// public partial class Article
/// {
///     [ScrapeField("h1")]
///     public string? Title { get; set; }
///
///     [ScrapeField(".views", Type = SchemaFieldType.Integer)]
///     public int Views { get; set; }
///
///     [ScrapeField(".tag", IsList = true)]
///     public List&lt;string&gt; Tags { get; set; } = new();
/// }
///
/// // Generated:
/// // - public static Schema Schema { get; }
/// // - public static Article Materialize(JsonObject json)
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ScrapeSchemaAttribute : Attribute { }
