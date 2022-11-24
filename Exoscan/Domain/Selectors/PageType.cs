using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Exoscan.Domain.Selectors;

[JsonConverter(typeof(StringEnumConverter))]
public enum PageType
{
    Dynamic,
    Static
}