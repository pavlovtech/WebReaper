using System.Text.Json.Serialization;
using WebReaper.Domain;
using WebReaper.Domain.PageActions;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;

namespace WebReaper.Serialization;

/// <summary>
/// The source-gen <see cref="JsonSerializerContext"/> for the crawl-state
/// payloads (ADR 0008). It generates metadata for the records and the
/// metadata-able members; the genuinely polymorphic / collection-shaped
/// members (the <see cref="Schema"/>/<see cref="SchemaElement"/> hierarchy,
/// <see cref="PageAction"/>'s <c>object[]</c>, the
/// <c>ImmutableQueue&lt;…&gt;</c> queues) are owned by hand-written
/// converters registered on <see cref="WebReaperJson"/>'s options and are
/// intentionally NOT listed here. Replaces the Newtonsoft
/// <c>TypeNameHandling.Auto</c> grammar of ADR 0003 (config) and the
/// <c>TypeNameHandling.None</c> Job path ADR 0005 left asymmetric.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ScraperConfig))]
[JsonSerializable(typeof(Job))]
[JsonSerializable(typeof(CookieDto[]))]
[JsonSerializable(typeof(List<PageAction>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(IEnumerable<string>))]
[JsonSerializable(typeof(PageType))]
[JsonSerializable(typeof(PageActionType))]
[JsonSerializable(typeof(DataType))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(bool))]
internal partial class WebReaperJsonContext : JsonSerializerContext;
