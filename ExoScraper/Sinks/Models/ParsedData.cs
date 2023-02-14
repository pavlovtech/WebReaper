using Newtonsoft.Json.Linq;

namespace ExoScraper.Sinks.Models;

public record ParsedData(string Url, JObject Data);