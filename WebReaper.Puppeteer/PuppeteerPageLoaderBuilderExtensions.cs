using WebReaper.Builders;

namespace WebReaper.Puppeteer;

/// <summary>
/// ADR-0009: the headless-browser page-load transport lives here, in the
/// WebReaper.Puppeteer satellite, registered over
/// <see cref="ScraperEngineBuilder"/>'s public <c>WithLoadTransport</c>
/// registration seam — not in core. Core no longer references
/// PuppeteerSharp/PuppeteerExtraSharp and is HTTP-only by default.
///
/// The seam is a factory: core invokes it at build time with the builder's
/// resolved cookie storage, optional proxy provider and logger — the same
/// collaborators the HTTP transport gets — so this stays parameterless and
/// the pre-7.0 default behaviour (one shared cookie container, issue #26;
/// the optional proxy applied the browser's way) is preserved exactly.
/// </summary>
public static class PuppeteerPageLoaderBuilderExtensions
{
    /// <summary>
    /// Use the Puppeteer/Chromium headless-browser transport for Dynamic
    /// pages (<c>GetWithBrowser</c> / <c>FollowWithBrowser</c> /
    /// <c>PaginateWithBrowser</c>). Required since 7.0.0 (ADR-0009) — without
    /// it a Dynamic load throws an actionable message. First Dynamic run
    /// downloads Chromium via Puppeteer. Since ADR-0050 the transport also
    /// receives the registered
    /// <see cref="WebReaper.Core.Actions.Abstract.IActionResolver"/> for
    /// <c>SemanticAct</c> dispatch.
    /// </summary>
    public static ScraperEngineBuilder WithPuppeteerPageLoader(this ScraperEngineBuilder builder) =>
        builder.WithLoadTransport((cookies, proxy, logger, actionResolver) =>
            new BrowserPageLoadTransport(cookies, proxy, logger, actionResolver));
}
