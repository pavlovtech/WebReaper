namespace WebReaper.Domain.Selectors;

// ADR 0008: the Newtonsoft [JsonConverter(StringEnumConverter)] attribute was
// removed — string-enum serialisation is now the AOT-safe
// JsonStringEnumConverter<PageType> registered on WebReaperJson. The Domain
// enum no longer references Newtonsoft.
/// <summary>
/// How a page is loaded: <see cref="Static"/> via HTTP, or
/// <see cref="Dynamic"/> via the headless-browser transport (the
/// WebReaper.Puppeteer satellite, ADR-0009) for JavaScript-rendered pages.
/// </summary>
public enum PageType
{
    /// <summary>Load through the headless browser (JavaScript-rendered).</summary>
    Dynamic,

    /// <summary>Load via a plain HTTP request (the default, core-only).</summary>
    Static
}
