namespace WebReaper.Domain.PageActions;

/// <summary>
/// One browser interaction performed on a dynamic page before scraping — built
/// via <see cref="WebReaper.Builders.PageActionBuilder"/> and interpreted by
/// the browser-transport satellites (WebReaper.Playwright, WebReaper.Cdp).
/// <para>
/// A closed sum (ADR-0035, ADR-0074, the ADR-0001 closed-sum pattern as <c>CrawlOutcome</c>):
/// exactly one of the ten nested arms, each carrying its own typed parameters;
/// no untyped <c>object[]</c>, no separate discriminant enum. Construct only via
/// the nested arms; the union is not extensible.
/// </para>
/// <para>
/// Implicit-timeout note (ADR-0074): <see cref="Fill"/> carries a 30 s
/// auto-wait on selector resolution (CDP polls every 50 ms; Playwright
/// auto-waits natively). Every other arm with a timeout
/// (<see cref="WaitForSelector"/>) takes it as an explicit
/// <c>TimeoutMs</c> field. The discipline: implicit when the timeout
/// is the safety net for the common case; explicit when it varies per call.
/// Compose <c>WaitForSelector(sel, custom_timeout) + Fill(sel, value)</c>
/// for a non-30s wait.
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
    /// Scroll the element matching a CSS selector into the viewport (ADR-0074).
    /// <para>
    /// Distinct from <see cref="ScrollToEnd"/>: <see cref="ScrollToEnd"/> scrolls
    /// the entire page to the bottom to trigger infinite-scroll loading;
    /// <see cref="ScrollIntoView"/> brings a specific element into view,
    /// typically before a click or assertion targets it.
    /// </para>
    /// <para>
    /// Implicit 30 s auto-wait: the selector is resolved against the page (via
    /// <c>WaitForSelectorAsync</c> on CDP, or Playwright's native auto-wait)
    /// before the scroll is issued. A selector that never appears in 30 s
    /// throws <see cref="TimeoutException"/>.
    /// </para>
    /// </summary>
    /// <param name="Selector">The CSS selector of the element to scroll into view.</param>
    public sealed record ScrollIntoView(string Selector) : PageAction;

    /// <summary>
    /// Resolve a natural-language intent to a concrete <see cref="PageAction"/>
    /// arm at runtime (ADR-0050). The first dispatch on each crawl invokes the
    /// registered <see cref="WebReaper.Core.Actions.Abstract.IActionResolver"/>
    /// — typically an LLM in the <c>WebReaper.AI</c> satellite — to produce a
    /// selector-based arm; the resolution is cached per-crawl by intent string,
    /// so every subsequent dispatch of the same intent runs the cached
    /// deterministic arm with no LLM call. The LLM-as-proposer /
    /// deterministic-as-decider pattern (ADR-0046, ADR-0047) applied to the
    /// action surface.
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

    /// <summary>
    /// Fill the element matching <paramref name="Selector"/> with
    /// <paramref name="Value"/> (ADR-0074). Implicit policies:
    /// <list type="bullet">
    ///   <item><b>Auto-wait, 30 s.</b> The selector is resolved before the fill
    ///   (CDP polls every 50 ms; Playwright auto-waits natively). A selector
    ///   that never appears throws <see cref="TimeoutException"/>.</item>
    ///   <item><b>Element-shape check.</b> The matched element must be an
    ///   <c>HTMLInputElement</c>, <c>HTMLTextAreaElement</c>, or
    ///   <c>isContentEditable</c>. Anything else throws.</item>
    ///   <item><b>Disabled check.</b> A disabled element throws.</item>
    ///   <item><b>Clear-before-fill.</b> Matches <c>page.FillAsync</c>
    ///   semantics; compose with <see cref="EvaluateExpression"/> to read the
    ///   current value first if append is needed.</item>
    ///   <item><b>Framework-observed events.</b> The CDP transport uses the
    ///   React-friendly native-setter trick (the property descriptor's
    ///   <c>value</c> setter, not <c>.value = x</c> directly), then dispatches
    ///   <c>focus</c>, <c>input</c>, <c>change</c> synchronously. Controlled
    ///   components in React / Vue / Svelte observe the change.</item>
    /// </list>
    /// </summary>
    /// <param name="Selector">CSS selector for the target text-input element.</param>
    /// <param name="Value">Text to fill into the element.</param>
    public sealed record Fill(string Selector, string Value) : PageAction;
}
