using System.Text.Json.Nodes;
using WebReaper.Core.Actions.Concrete;
using WebReaper.Domain.PageActions;

namespace WebReaper.Cdp;

/// <summary>
/// The ADR-0035 closed-sum dispatch table for the CDP transport. Extracted
/// from <see cref="CdpPageLoadTransport"/> so the table is unit-testable
/// against a <see cref="ICdpSession"/> fake (no real WebSocket) — see
/// <c>WebReaper.Cdp.Tests.CdpPageActionDispatchTests</c>. The transport
/// composes by delegating each <see cref="PageAction"/> arm.
/// </summary>
/// <remarks>
/// Internal: the dispatch shape is an implementation detail of the
/// satellite, not part of its public contract. Adding an arm changes
/// <see cref="PerformAsync"/> AND the matching <c>FakeCdpSession</c>
/// assertion AND the <see cref="PageAction"/> sum — three places, one
/// closed-sum widening.
/// </remarks>
internal static class CdpPageActionDispatcher
{
    /// <summary>Dispatch one <see cref="PageAction"/> arm against
    /// <paramref name="session"/> + <paramref name="sessionId"/>. Recursive
    /// only via <see cref="PageAction.SemanticAct"/> → resolver → concrete
    /// arm; the recursion terminates after at most one re-dispatch
    /// (ADR-0050 <see cref="SemanticActCoordinator"/> rejects nested
    /// <c>SemanticAct</c>).</summary>
    public static async Task PerformAsync(
        ICdpSession session,
        string sessionId,
        PageAction action,
        SemanticActCoordinator semanticActCoordinator,
        CancellationToken ct)
    {
        switch (action)
        {
            case PageAction.Click a:
                // Use the page's own click() to honour pointer-events/disabled.
                await EvaluateAsync(session, sessionId,
                    $"(() => {{ const el = document.querySelector({JsonStringLiteral(a.Selector)}); if (!el) throw new Error('Selector not found: ' + {JsonStringLiteral(a.Selector)}); el.click(); }})()",
                    ct);
                break;
            case PageAction.Wait a:
                await Task.Delay(a.Milliseconds, ct);
                break;
            case PageAction.ScrollToEnd:
                await EvaluateAsync(session, sessionId,
                    "window.scrollTo(0, document.body.scrollHeight)", ct);
                break;
            case PageAction.EvaluateExpression a:
                await EvaluateAsync(session, sessionId, a.Expression, ct);
                break;
            case PageAction.WaitForSelector a:
                await WaitForSelectorAsync(session, sessionId, a.Selector, a.TimeoutMs, ct);
                break;
            case PageAction.WaitForNetworkIdle:
                // ADR-0057: real per-session Network.* event tracking with a
                // 500 ms debounce + 30 s total timeout. Replaces the v10.0.0
                // Task.Delay(500) placeholder.
                await session.WaitForNetworkIdleAsync(sessionId, ct: ct);
                break;
            case PageAction.ScrollIntoView a:
                // ADR-0074: auto-wait 30 s for selector, then scroll into view.
                // Distinct from ScrollToEnd (which scrolls the page to trigger
                // infinite-scroll loading); this brings a specific element into
                // the viewport for click-targeting.
                await WaitForSelectorAsync(session, sessionId, a.Selector, 30_000, ct);
                await EvaluateAsync(session, sessionId,
                    $"document.querySelector({JsonStringLiteral(a.Selector)}).scrollIntoView()",
                    ct);
                break;
            case PageAction.SemanticAct a:
                await semanticActCoordinator.DispatchAsync(
                    a.Intent,
                    getHtmlAsync: token => GetHtmlAsync(session, sessionId, token),
                    dispatch: (arm, token) => PerformAsync(session, sessionId, arm, semanticActCoordinator, token),
                    ct);
                break;
            case PageAction.Press a:
                // ADR-0074: map the Playwright-style key string to the four CDP
                // Input.dispatchKeyEvent fields via the CdpKeyMapper deep module,
                // then fire keyDown + keyUp against whichever element holds focus.
                var cdpKey = CdpKeyMapper.Map(a.Key);
                await session.SendAsync("Input.dispatchKeyEvent",
                    new JsonObject
                    {
                        ["type"] = "keyDown",
                        ["key"] = cdpKey.Key,
                        ["code"] = cdpKey.Code,
                        ["windowsVirtualKeyCode"] = cdpKey.WindowsVirtualKeyCode,
                        ["modifiers"] = cdpKey.Modifiers,
                    }, sessionId, ct);
                await session.SendAsync("Input.dispatchKeyEvent",
                    new JsonObject
                    {
                        ["type"] = "keyUp",
                        ["key"] = cdpKey.Key,
                        ["code"] = cdpKey.Code,
                        ["windowsVirtualKeyCode"] = cdpKey.WindowsVirtualKeyCode,
                        ["modifiers"] = cdpKey.Modifiers,
                    }, sessionId, ct);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(action), action.GetType().Name, "unhandled PageAction arm");
        }
    }

    /// <summary>Poll-on-50 ms for <paramref name="selector"/> until it
    /// resolves on the page, or <paramref name="timeoutMs"/> elapses
    /// (<see cref="TimeoutException"/>).</summary>
    internal static async Task WaitForSelectorAsync(
        ICdpSession session, string sessionId, string selector, int timeoutMs, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var found = await EvaluateAsync(session, sessionId,
                $"!!document.querySelector({JsonStringLiteral(selector)})", ct);
            if (found == "true") return;
            await Task.Delay(50, ct);
        }
        throw new TimeoutException($"Timed out waiting {timeoutMs}ms for selector: {selector}");
    }

    /// <summary>Run <c>Runtime.evaluate</c> against the session; returns the
    /// boxed primitive (string-typed if STJ deserialised it as string,
    /// the JSON-string otherwise) or <c>null</c> on an undefined result.
    /// </summary>
    internal static async Task<string?> EvaluateAsync(
        ICdpSession session, string sessionId, string expression, CancellationToken ct)
    {
        var result = await session.SendAsync("Runtime.evaluate",
            new JsonObject
            {
                ["expression"] = expression,
                ["awaitPromise"] = true,
                ["returnByValue"] = true,
            },
            sessionId, ct);

        if (result["exceptionDetails"] is JsonObject ex)
        {
            var text = ex["text"]?.GetValue<string>() ?? "Runtime.evaluate failed";
            throw new CdpException(text);
        }

        var value = result["result"]?["value"];
        if (value is null) return null;
        return value.GetValueKind() switch
        {
            System.Text.Json.JsonValueKind.String => value.GetValue<string>(),
            _ => value.ToJsonString(),
        };
    }

    internal static Task<string> GetHtmlAsync(ICdpSession session, string sessionId, CancellationToken ct) =>
        EvaluateAsync(session, sessionId, "document.documentElement.outerHTML", ct)!;

    internal static string JsonStringLiteral(string s)
    {
        // JSON-escape via JsonValue; embeds correctly into a JS expression.
        return JsonValue.Create(s)!.ToJsonString();
    }
}
