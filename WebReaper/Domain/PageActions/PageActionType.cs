namespace WebReaper.Domain.PageActions;

// ADR 0008: the Newtonsoft [JsonConverter(StringEnumConverter)] attribute was
// removed — string-enum serialisation is now the AOT-safe
// JsonStringEnumConverter<PageActionType> registered on WebReaperJson. The
// Domain enum no longer references Newtonsoft.
/// <summary>
/// The browser interactions a <see cref="PageAction"/> can perform on a
/// dynamic page before it is scraped (executed by the WebReaper.Puppeteer
/// satellite, ADR-0009).
/// </summary>
public enum PageActionType
{
    /// <summary>Click the element matching a CSS selector.</summary>
    Click,

    /// <summary>Wait a fixed number of milliseconds.</summary>
    Wait,

    /// <summary>Scroll to the bottom of the page (e.g. to trigger
    /// infinite-scroll loading).</summary>
    ScrollToEnd,

    /// <summary>Evaluate a JavaScript expression in the page context.</summary>
    EvaluateExpression,

    /// <summary>Wait until an element matching a CSS selector appears.</summary>
    WaitForSelector,

    /// <summary>Wait until the page's network activity goes idle.</summary>
    WaitForNetworkIdle
}
