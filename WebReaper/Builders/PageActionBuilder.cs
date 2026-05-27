using WebReaper.Domain.PageActions;

namespace WebReaper.Builders;

/// <summary>
/// Fluent builder for the ordered list of browser interactions performed on a
/// dynamic page before it is scraped — click, wait, scroll, evaluate JS,
/// wait-for-selector / network-idle — plus <c>Repeat*</c> combinators that
/// replay the previously-added action. Passed to
/// <see cref="ConfigBuilder.GetWithBrowser"/> /
/// <c>FollowWithBrowser</c> / <c>PaginateWithBrowser</c>;
/// <see cref="Build"/> returns the accumulated list. The actions run in the
/// WebReaper.Puppeteer satellite at run time (core is HTTP-only, ADR-0009).
/// Every method returns the same instance for chaining.
/// </summary>
public class PageActionBuilder
{
    private readonly List<PageAction> _pageActions = new();

    /// <summary>Click the element matching the CSS <paramref name="selector"/>.</summary>
    public PageActionBuilder Click(string selector)
    {
        _pageActions.Add(new PageAction.Click(selector));
        return this;
    }

    /// <summary>Wait a fixed <paramref name="milliseconds"/> (e.g. to let
    /// scripted content settle).</summary>
    public PageActionBuilder Wait(int milliseconds)
    {
        _pageActions.Add(new PageAction.Wait(milliseconds));
        return this;
    }

    /// <summary>Scroll to the bottom of the page once — typically to trigger
    /// infinite-scroll loading (combine with <see cref="Repeat"/> /
    /// <see cref="RepeatAndWaitForNetworkIdle"/> for multiple loads).</summary>
    public PageActionBuilder ScrollToEnd()
    {
        _pageActions.Add(new PageAction.ScrollToEnd());
        return this;
    }

    // The Repeat* methods replay the previously-added action. With no prior
    // action there is nothing to replay: surface that as a clear builder
    // misuse instead of the raw ArgumentOutOfRangeException from _pageActions[^1].
    private PageAction LastActionToRepeat(string method) =>
        _pageActions.Count > 0
            ? _pageActions[^1]
            : throw new InvalidOperationException(
                $"{method} can only be called after at least one page action has been added — there is no action to repeat.");

    /// <summary>
    /// Replay the previously-added action <paramref name="times"/> more times,
    /// each followed by a <paramref name="milliseconds"/> wait (e.g.
    /// scroll-then-pause for paged infinite scroll).
    /// </summary>
    /// <exception cref="InvalidOperationException">called before any action
    /// was added (8.0.0 fail-fast).</exception>
    public PageActionBuilder RepeatWithDelay(int times, int milliseconds)
    {
        var lastEl = LastActionToRepeat(nameof(RepeatWithDelay));

        _pageActions.AddRange(
            Enumerable.Range(1, times)
                .Select(_ => new[]
                {
                    lastEl,
                    new PageAction.Wait(milliseconds)
                })
                .SelectMany(x => x));

        return this;
    }

    /// <summary>
    /// Replay the previously-added action <paramref name="times"/> more times,
    /// each followed by a wait-for-network-idle (the load-driven analogue of
    /// <see cref="RepeatWithDelay"/> — wait for fetches to settle, not a fixed
    /// delay).
    /// </summary>
    /// <exception cref="InvalidOperationException">called before any action
    /// was added.</exception>
    public PageActionBuilder RepeatAndWaitForNetworkIdle(int times)
    {
        var lastEl = LastActionToRepeat(nameof(RepeatAndWaitForNetworkIdle));

        _pageActions.AddRange(
            Enumerable.Range(1, times)
                .Select(_ => new[]
                {
                    lastEl,
                    new PageAction.WaitForNetworkIdle()
                })
                .SelectMany(x => x));

        return this;
    }

    /// <summary>
    /// Replay the previously-added action <paramref name="times"/> more times
    /// back-to-back, with nothing interleaved.
    /// </summary>
    /// <exception cref="InvalidOperationException">called before any action
    /// was added.</exception>
    public PageActionBuilder Repeat(int times)
    {
        var lastEl = LastActionToRepeat(nameof(Repeat));
        _pageActions.AddRange(Enumerable.Range(1, times).Select(_ => lastEl));
        return this;
    }

    /// <summary>Evaluate a JavaScript <paramref name="expression"/> in the
    /// page context.</summary>
    public PageActionBuilder EvaluateExpression(string expression)
    {
        _pageActions.Add(new PageAction.EvaluateExpression(expression));
        return this;
    }

    /// <summary>Wait until an element matching <paramref name="selector"/>
    /// appears, up to <paramref name="timeout"/> ms.</summary>
    public PageActionBuilder WaitForSelector(string selector, int timeout)
    {
        _pageActions.Add(new PageAction.WaitForSelector(selector, timeout));
        return this;
    }

    /// <summary>Wait until the page's network activity goes idle (no
    /// fixed duration).</summary>
    public PageActionBuilder WaitForNetworkIdle()
    {
        _pageActions.Add(new PageAction.WaitForNetworkIdle());
        return this;
    }

    /// <summary>
    /// Resolve a natural-language <paramref name="intent"/> (e.g. "click sign in")
    /// to a concrete <see cref="PageAction"/> at runtime via the registered
    /// <see cref="WebReaper.Core.Actions.Abstract.IActionResolver"/> (ADR-0050).
    /// The first dispatch on each crawl calls the resolver; the resolution is
    /// cached per-crawl by intent string and reused on every subsequent
    /// dispatch — the deterministic path is the hot path.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="intent"/> is
    /// null/empty/whitespace.</exception>
    public PageActionBuilder SemanticAct(string intent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intent);
        _pageActions.Add(new PageAction.SemanticAct(intent));
        return this;
    }

    /// <summary>
    /// Send the keyboard <paramref name="key"/> to the currently-focused element
    /// (ADR-0074). Key strings follow Playwright's format: single printable
    /// characters (<c>"a"</c>, <c>"Enter"</c>, <c>"Tab"</c>), named keys, and
    /// modifier-prefixed combos (<c>"Control+A"</c>, <c>"Shift+Tab"</c>). The
    /// dispatch goes to whichever element holds focus at the moment; pair with
    /// <see cref="Click"/> or <see cref="WaitForSelector"/> first when focus
    /// placement is uncertain.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="key"/> is
    /// null/empty/whitespace.</exception>
    public PageActionBuilder Press(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _pageActions.Add(new PageAction.Press(key));
        return this;
    }

    /// <summary>The accumulated actions, in the order they were added.</summary>
    public List<PageAction> Build()
    {
        return _pageActions;
    }
}
