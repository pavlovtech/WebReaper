using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace WebReaper.Domain.Selectors;

[JsonConverter(typeof(StringEnumConverter))]
public enum PageType
{
    Dynamic,
    Static
}