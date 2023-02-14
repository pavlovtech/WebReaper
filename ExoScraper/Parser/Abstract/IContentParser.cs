using ExoScraper.Domain.Parsing;
using Newtonsoft.Json.Linq;

namespace ExoScraper.Parser.Abstract;

public interface IContentParser
{
    Task<JObject> ParseAsync(string html, Schema? schema);
}