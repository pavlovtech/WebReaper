using Newtonsoft.Json.Linq;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Abstract;

public interface IContentParser
{
    Task<JObject> ParseAsync(string html, Schema? schema);
}