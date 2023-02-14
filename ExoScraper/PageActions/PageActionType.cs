using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ExoScraper.PageActions;

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