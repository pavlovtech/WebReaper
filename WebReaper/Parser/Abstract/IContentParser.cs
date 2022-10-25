using Newtonsoft.Json.Linq;
using WebReaper.Domain.Parsing;

namespace WebReaper.Parser.Abstract;

public interface IContentParser
{
    JObject Parse(string html, Schema? schema);
}