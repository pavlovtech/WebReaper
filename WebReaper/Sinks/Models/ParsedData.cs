using Newtonsoft.Json.Linq;

namespace WebReaper.Sinks.Models;

public record ParsedData(string SiteId, string Url, JObject Data);