using Newtonsoft.Json.Linq;

namespace Exoscan.Sinks.Models;

public record ParsedData(string Url, JObject Data);