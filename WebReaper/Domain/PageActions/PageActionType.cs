namespace WebReaper.Domain.PageActions;

// ADR 0008: the Newtonsoft [JsonConverter(StringEnumConverter)] attribute was
// removed — string-enum serialisation is now the AOT-safe
// JsonStringEnumConverter<PageActionType> registered on WebReaperJson. The
// Domain enum no longer references Newtonsoft.
public enum PageActionType
{
    Click,
    Wait,
    ScrollToEnd,
    EvaluateExpression,
    WaitForSelector,
    WaitForNetworkIdle
}
