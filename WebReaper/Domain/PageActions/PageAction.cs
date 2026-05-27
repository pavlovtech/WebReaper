namespace WebReaper.Domain.PageActions;

/// <summary>
/// One browser interaction performed on a dynamic page before scraping — built
/// via <see cref="WebReaper.Builders.PageActionBuilder"/> and interpreted by
/// the WebReaper.Puppeteer satellite (ADR-0009).
/// <para>
/// A closed sum (ADR-0035, the ADR-0001 closed-sum pattern, as <c>CrawlOutcome</c>):
/// exactly one of the seven nested arms, each carrying its own typed parameters —
/// no untyped <c>object[]</c>, no separate discriminant enum. Construct only via
/// the nested arms; the union is not extensible.
/// </para>
/// </summary>
public abstract record PageAction
{
    private PageAction() { }

    /// <summary>Click the element matching a CSS selector.</summary>
    /// <param name="Selector">The CSS selector of the element to click.</param>
    public sealed record Click(string Selector) : PageAction;

    /// <summary>Wait a fixed number of milliseconds (e.g. to let scripted
    /// content settle).</summary>
    /// <param name="Milliseconds">How long to wait.</param>
    public sealed record Wait(int Milliseconds) : PageAction;

    /// <summary>Scroll to the bottom of the page (e.g. to trigger
    /// infinite-scroll loading).</summary>
    public sealed record ScrollToEnd : PageAction;

    /// <summary>Evaluate a JavaScript expression in the page context.</summary>
    /// <param name="Expression">The JavaScript expression to evaluate.</param>
    public sealed record EvaluateExpression(string Expression) : PageAction;

    /// <summary>Wait until an element matching a CSS selector appears.</summary>
    /// <param name="Selector">The CSS selector to wait for.</param>
    /// <param name="TimeoutMs">How long to wait for it, in milliseconds.</param>
    public sealed record WaitForSelector(string Selector, int TimeoutMs) : PageAction;

    /// <summary>Wait until the page's network activity goes idle.</summary>
    public sealed record WaitForNetworkIdle : PageAction;

    /// <summary>
    /// Resolve a natural-language intent to a concrete <see cref="PageAction"/>
    /// arm at runtime (ADR-0050). The first dispatch on each crawl invokes the
    /// registered <see cref="WebReaper.Core.Actions.Abstract.IActionResolver"/>
    /// — typically an LLM in the <c>WebReaper.AI</c> satellite — to produce a
    /// selector-based arm (<see cref="Click"/>, <see cref="WaitForSelector"/>,
    /// <see cref="Wait"/>, or <see cref="EvaluateExpression"/>); the resolution
    /// is cached per-crawl by intent string, so every subsequent dispatch of
    /// the same intent runs the cached deterministic arm with no LLM call. The
    /// LLM-as-proposer / deterministic-as-decider pattern (ADR-0046, ADR-0047)
    /// applied to the action surface.
    /// </summary>
    /// <param name="Intent">The natural-language intent (e.g. "click sign in").</param>
    public sealed record SemanticAct(string Intent) : PageAction;

    /// <summary>
    /// Send a keyboard key press to the currently-focused element (ADR-0074).
    /// Dispatches a <c>keyDown</c> followed by a <c>keyUp</c> event with no
    /// selector resolution: whichever element holds focus at the moment of
    /// dispatch receives the event. Pair with <see cref="Click"/> or
    /// <see cref="WaitForSelector"/> first when focus is not already in the
    /// right element.
    /// <para>
    /// Key strings follow Playwright's format: single printable characters
    /// (<c>"a"</c>, <c>"A"</c>, <c>"1"</c>), named keys (<c>"Enter"</c>,
    /// <c>"Tab"</c>, <c>"Escape"</c>, <c>"Backspace"</c>, <c>"Delete"</c>,
    /// <c>"ArrowUp"</c>, <c>"ArrowDown"</c>, <c>"ArrowLeft"</c>,
    /// <c>"ArrowRight"</c>, <c>"Home"</c>, <c>"End"</c>, <c>"PageUp"</c>,
    /// <c>"PageDown"</c>, <c>"Space"</c>, <c>"F1"</c>-<c>"F12"</c>), and
    /// modifier-prefixed combos (<c>"Control+A"</c>, <c>"Shift+Tab"</c>,
    /// <c>"Meta+C"</c>, <c>"Alt+F4"</c>, <c>"Control+Shift+K"</c>).
    /// </para>
    /// <para>
    /// The CDP transport maps the key string to the four CDP
    /// <c>Input.dispatchKeyEvent</c> fields via the static
    /// <c>CdpKeyMapper</c> deep module. An unknown key string throws
    /// <see cref="ArgumentException"/> at dispatch time. Playwright accepts
    /// the same key-string format natively.
    /// </para>
    /// </summary>
    /// <param name="Key">The Playwright-style key string (e.g. <c>"Enter"</c>,
    /// <c>"Control+A"</c>, <c>"a"</c>).</param>
    public sealed record Press(string Key) : PageAction;
}
