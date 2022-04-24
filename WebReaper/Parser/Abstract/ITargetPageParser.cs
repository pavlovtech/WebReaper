using Newtonsoft.Json.Linq;
using WebReaper.Domain;

interface ITargetPageParser
{
    JObject Parse(string html, SchemaElement[] schema);
}