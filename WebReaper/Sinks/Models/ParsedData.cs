using Newtonsoft.Json.Linq;

namespace WebReaper.Sinks.Models;

public record ParsedData(string Url, JObject Data);