using WebReaper.Core.Loaders.Abstract;

namespace WebReaper.Core.Loaders.Concrete;

/// <summary>
/// The default dynamic-page <see cref="IPageLoadTransport"/> when no browser
/// transport is registered (ADR-0009). Core is HTTP-only by default; the
/// headless-browser transport ships in one of two satellites:
/// <c>WebReaper.Playwright</c> (Microsoft.Playwright-backed; the modern default
/// per ADR-0053) or <c>WebReaper.Cdp</c> (raw CDP; AOT-clean; the bedrock for
/// stealth Chromium fork backends per ADR-0052).
/// A Dynamic-page load with no <c>WithLoadTransport</c> registration fails
/// here with an actionable message rather than a null-ref. ADR-0004's
/// one-<see cref="IPageLoader"/> / two-<see cref="IPageLoadTransport"/>
/// dispatcher is unchanged — only the default composition of the dynamic slot
/// moved out of core.
/// </summary>
internal sealed class BrowserNotConfiguredPageLoadTransport : IPageLoadTransport
{
    public Task<string> LoadAsync(PageRequest request, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "Dynamic (headless-browser) page loading requires a browser transport satellite. " +
            "Wire one of:\n" +
            "  • .WithPlaywrightPageLoader(...) — add WebReaper.Playwright (modern Microsoft.Playwright SDK; multi-browser).\n" +
            "  • .WithCdpPageLoader(...) — add WebReaper.Cdp (raw CDP; AOT-clean; required for stealth backends like CloakBrowser).\n" +
            "See ADR-0052 / ADR-0053 / ADR-0054.");
}
