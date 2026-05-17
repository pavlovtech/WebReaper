using System.Text.Json.Nodes;

namespace WebReaper.Sinks.Models;

// ADR 0008: the extracted record's payload is a System.Text.Json JsonObject
// (the typed terminal of the one Schema fold), not a Newtonsoft JObject.
// Major SemVer — the JObject public surface changed (precedent ADR 0004).
public record ParsedData(string Url, JsonObject Data);