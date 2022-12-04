using Exoscan.Domain.Parsing;
using Newtonsoft.Json.Linq;

namespace Exoscan.Parser.Abstract;

public interface IContentParser
{
    Task<JObject> ParseAsync(string html, Schema? schema);
}