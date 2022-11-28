using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Exoscan.PageActions;

[JsonConverter(typeof(StringEnumConverter))]
public enum PageActionType
{
    Click,
    Wait,
    ScrollToEnd,
    EvaluateExpression,
    WaitForSelector,
    WaitForNetworkIdle
}