using System.Runtime.CompilerServices;
using Microsoft.Playwright;
using WebReaper.Core.Actions.Concrete;
using WebReaper.Domain.PageActions;

[assembly: InternalsVisibleTo("WebReaper.Playwright.Tests")]

namespace WebReaper.Playwright;

/// <summary>
/// The ADR-0035 closed-sum dispatch table for the Playwright transport.
/// Extracted from <see cref="PlaywrightPageLoadTransport"/> (mirroring the CDP
/// transport's <c>CdpPageActionDispatcher</c>) so the table is unit-testable
/// against a mocked <see cref="IPage"/> — see
/// <c>WebReaper.Playwright.Tests.PlaywrightPageActionDispatchTests</c>. That
/// closes ADR-0078 Axis A's deferred follow-up: the "every <see cref="PageAction"/>
/// arm is handled" guarantee is now execution-proven for this transport too,
/// not only CDP. The transport composes by delegating each arm.
/// </summary>
/// <remarks>
/// Internal: the dispatch shape is an implementation detail of the satellite,
/// not part of its public contract. All ten arms use Playwright's native
/// methods (the four-arm Puppeteer gap closed in ADR-0053).
/// </remarks>
internal static class PlaywrightPageActionDispatcher
{
    /// <summary>Dispatch one <see cref="PageAction"/> arm against
    /// <paramref name="page"/>. Recursive only via
    /// <see cref="PageAction.SemanticAct"/> → resolver → concrete arm; the
    /// recursion terminates after at most one re-dispatch (ADR-0050
    /// <see cref="SemanticActCoordinator"/> rejects nested <c>SemanticAct</c>).</summary>
    public static async Task PerformAsync(
        IPage page,
        PageAction action,
        SemanticActCoordinator semanticActCoordinator,
        CancellationToken ct)
    {
        switch (action)
        {
            case PageAction.Click a:
                await page.ClickAsync(a.Selector);
                break;
            case PageAction.Wait a:
                await Task.Delay(a.Milliseconds, ct);
                break;
            case PageAction.ScrollToEnd:
                await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)");
                break;
            case PageAction.EvaluateExpression a:
                await page.EvaluateAsync(a.Expression);
                break;
            case PageAction.WaitForSelector a:
                await page.WaitForSelectorAsync(a.Selector, new PageWaitForSelectorOptions
                {
                    Timeout = a.TimeoutMs,
                });
                break;
            case PageAction.WaitForNetworkIdle:
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                break;
            case PageAction.ScrollIntoView a:
                // ADR-0074: Playwright's ScrollIntoViewIfNeededAsync natively
                // auto-waits for the element and scrolls it into the viewport.
                await page.Locator(a.Selector).ScrollIntoViewIfNeededAsync();
                break;
            case PageAction.SemanticAct a:
                await semanticActCoordinator.DispatchAsync(
                    a.Intent,
                    getHtmlAsync: _ => page.ContentAsync(),
                    dispatch: (arm, token) => PerformAsync(page, arm, semanticActCoordinator, token),
                    ct);
                break;
            case PageAction.Press a:
                // ADR-0074: Playwright accepts the same key-string format natively.
                await page.Keyboard.PressAsync(a.Key);
                break;
            case PageAction.Fill a:
                // ADR-0074: one line; native auto-wait + clear + framework events
                // handled by Playwright's page.FillAsync.
                await page.FillAsync(a.Selector, a.Value);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(action), action.GetType().Name, "unhandled PageAction arm");
        }
    }
}
