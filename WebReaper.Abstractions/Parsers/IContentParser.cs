using Newtonsoft.Json.Linq;
using WebReaper.Domain.Schema;

namespace WebReaper.Abstractions.Parsers;

public interface IContentParser
{
    JObject Parse(string html, SchemaElement[] schema);
}