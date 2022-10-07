using Newtonsoft.Json.Linq;
using WebReaper.Core.Domain.Parsing;

namespace WebReaper.Core.Parser.Abstract;

public interface IContentParser
{
    JObject Parse(string html, Schema schema);
}