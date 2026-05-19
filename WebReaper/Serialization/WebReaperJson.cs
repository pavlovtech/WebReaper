using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using WebReaper.Domain;
using WebReaper.Domain.PageActions;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;
using WebReaper.Serialization.Converters;

namespace WebReaper.Serialization;

/// <summary>
/// The one home for the crawl-state serialization grammar (ADR 0008),
/// replacing the Newtonsoft <c>TypeNameHandling.Auto</c> of ADR 0003's config
/// payload shell and the asymmetric <c>TypeNameHandling.None</c> Job path ADR
/// 0005 named. Concrete, not an interface — there is exactly one serialization
/// grammar; the variation is the converters, not implementations of a seam
/// (the ADR 0005 <c>RedisConnectionPool</c> reasoning). AOT-clean: a source-gen
/// <see cref="WebReaperJsonContext"/> resolver plus hand-written converters,
/// serialised through the generated <see cref="JsonTypeInfo{T}"/> (the pattern
/// the Phase-0 spike proved zero-warning under <c>PublishAot</c>).
/// </summary>
public static class WebReaperJson
{
    private static readonly JsonSerializerOptions Options = Build();

    private static JsonSerializerOptions Build()
    {
        var o = new JsonSerializerOptions
        {
            TypeInfoResolver = WebReaperJsonContext.Default,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        o.Converters.Add(new SchemaJsonConverter());
        o.Converters.Add(new SchemaElementJsonConverter());
        o.Converters.Add(new PageActionJsonConverter());
        o.Converters.Add(new SelectorChainJsonConverter());
        o.Converters.Add(new BacklinkQueueJsonConverter());
        o.Converters.Add(new JsonStringEnumConverter<PageType>());
        o.Converters.Add(new JsonStringEnumConverter<PageActionType>());
        o.Converters.Add(new JsonStringEnumConverter<DataType>());
        return o;
    }

    // GetTypeInfo over a source-gen resolver is AOT-safe and composes the
    // registered converters into the generated metadata (spike-proven).
    private static JsonTypeInfo<T> Info<T>() => (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T));

    /// <summary>Serialise a <see cref="ScraperConfig"/> to the WebReaper JSON
    /// grammar — what <c>IScraperConfigStorage</c> persists so an in-process
    /// or distributed worker reads identical settings.</summary>
    public static string SerializeConfig(ScraperConfig config)
        => JsonSerializer.Serialize(config, Info<ScraperConfig>());

    /// <summary>Parse a <see cref="ScraperConfig"/> from the WebReaper JSON
    /// grammar.</summary>
    /// <exception cref="JsonException">the payload deserialised to
    /// null.</exception>
    public static ScraperConfig DeserializeConfig(string json)
        => JsonSerializer.Deserialize(json, Info<ScraperConfig>())
           ?? throw new JsonException("ScraperConfig deserialised to null");

    /// <summary>Serialise a <see cref="Job"/> to the WebReaper JSON grammar —
    /// the wire form the distributed schedulers (Redis / Azure Service Bus /
    /// SQLite) enqueue and replay (ADR-0008).</summary>
    public static string SerializeJob(Job job)
        => JsonSerializer.Serialize(job, Info<Job>());

    /// <summary>Parse a <see cref="Job"/> from the WebReaper JSON
    /// grammar.</summary>
    /// <exception cref="JsonException">the payload deserialised to
    /// null.</exception>
    public static Job DeserializeJob(string json)
        => JsonSerializer.Deserialize(json, Info<Job>())
           ?? throw new JsonException("Job deserialised to null");

    // internal: CookieDto is an internal serialization detail, not public API.
    internal static string SerializeCookies(CookieDto[] cookies)
        => JsonSerializer.Serialize(cookies, Info<CookieDto[]>());

    internal static CookieDto[] DeserializeCookies(string json)
        => JsonSerializer.Deserialize(json, Info<CookieDto[]>()) ?? [];
}
