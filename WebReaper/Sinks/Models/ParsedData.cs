using System.Text.Json.Nodes;

namespace WebReaper.Sinks.Models;

// ADR 0008: the extracted record's payload is a System.Text.Json JsonObject
// (the typed terminal of the one Schema fold), not a Newtonsoft JObject.
// Major SemVer — the JObject public surface changed (precedent ADR 0004).
/// <summary>
/// One scraped record: the page it came from and the JSON the
/// <see cref="WebReaper.Domain.Parsing.Schema"/> fold produced. Emitted to
/// every <see cref="WebReaper.Sinks.Abstract.IScraperSink"/> and to
/// <c>Subscribe</c> callbacks by the Crawl driver (ADR-0022) for each target
/// page.
///
/// ADR-0031: construction folds the page <see cref="Url"/> into
/// <see cref="Data"/> under the key <c>"url"</c>, so <see cref="Data"/> is the
/// canonical emitted record — every sink writes it as-is, none re-merges the
/// URL. <c>"url"</c> is therefore a reserved key: a Schema field named
/// <c>url</c> is overwritten by the page URL.
/// </summary>
/// <param name="Url">The page the data was scraped from.</param>
/// <param name="Data">The extracted fields as a System.Text.Json object; the
/// page URL is folded in under <c>"url"</c> at construction.</param>
public record ParsedData(string Url, JsonObject Data)
{
    /// <summary>The extracted fields as a System.Text.Json object, with the
    /// page <see cref="Url"/> folded in under <c>"url"</c> — the canonical
    /// emitted record (ADR-0031).</summary>
    public JsonObject Data { get; init; } = MergeUrl(Url, Data);

    // ADR-0031: the URL-merge has one home — here. Every ParsedData is built
    // with the page URL folded into Data, so no sink re-merges it (and the
    // merge cannot drift, as ConsoleSink's missing copy once did). Idempotent.
    private static JsonObject MergeUrl(string url, JsonObject data)
    {
        data["url"] = url;
        return data;
    }
}
