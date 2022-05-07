using Newtonsoft.Json.Linq;
using WebReaper.Domain.Parsing;

namespace WebReaper.Abstractions.Parsers;

public interface IContentParser
{
    JObject Parse(string html, Schema schema);
}