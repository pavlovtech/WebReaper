using Exoscan.Domain.Parsing;
using Newtonsoft.Json.Linq;

namespace Exoscan.Parser.Abstract;

public interface IContentParser
{
    JObject Parse(string html, Schema? schema);
}