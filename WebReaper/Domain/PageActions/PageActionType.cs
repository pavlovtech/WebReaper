using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace WebReaper.Domain.PageActions;

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