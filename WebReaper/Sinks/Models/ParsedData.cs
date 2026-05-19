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
/// </summary>
/// <param name="Url">The page the data was scraped from.</param>
/// <param name="Data">The extracted fields as a System.Text.Json object.</param>
public record ParsedData(string Url, JsonObject Data);
