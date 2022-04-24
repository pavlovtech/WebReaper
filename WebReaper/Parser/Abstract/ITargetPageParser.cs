using Newtonsoft.Json.Linq;
using WebReaper.Domain;

public interface IContentParser
{
    JObject Parse(string html, SchemaElement[] schema);
}