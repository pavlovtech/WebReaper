namespace WebReaper.Playwright;

/// <summary>
/// The three browser engines Microsoft.Playwright supports. Default for
/// <see cref="PlaywrightPageLoaderBuilderExtensions.WithPlaywrightPageLoader"/>
/// is <see cref="Chromium"/> — matches the deleted Puppeteer satellite's
/// behaviour. Firefox/WebKit are opt-in (ADR-0053).
/// </summary>
public enum PlaywrightBrowser
{
    /// <summary>Chromium (and Chromium-derived browsers via
    /// <see cref="PlaywrightLaunchOptions.Channel"/>).</summary>
    Chromium,

    /// <summary>Firefox (the Playwright-patched build, not stock Firefox).</summary>
    Firefox,

    /// <summary>WebKit — closest to Safari rendering. macOS-only WebKit
    /// features may not all be reachable on other OSes.</summary>
    Webkit,
}

/// <summary>
/// Launch-time options for the Playwright transport. A thin wrapper around
/// the most commonly-tuned fields of Microsoft.Playwright's
/// <c>BrowserTypeLaunchOptions</c>; full fidelity exposed via
/// <see cref="ConfigureLaunchOptions"/>.
/// </summary>
public sealed class PlaywrightLaunchOptions
{
    /// <summary>Run the browser headless. Default <c>true</c>.</summary>
    public bool Headless { get; set; } = true;

    /// <summary>Distribution channel (Chromium only): <c>"chrome"</c>,
    /// <c>"msedge"</c>, <c>"chrome-beta"</c>, … When set, Playwright uses
    /// the system-installed browser of that channel instead of the bundled
    /// Chromium.</summary>
    public string? Channel { get; set; }

    /// <summary>Absolute path to a browser executable; overrides
    /// <see cref="Channel"/>. Default <c>null</c> = use Playwright's
    /// bundled browser.</summary>
    public string? ExecutablePath { get; set; }

    /// <summary>Extra command-line arguments to pass to the browser binary.</summary>
    public IReadOnlyList<string>? Args { get; set; }

    /// <summary>Optional callback to mutate the underlying
    /// <c>BrowserTypeLaunchOptions</c> just before launch — escape hatch
    /// for any field this wrapper doesn't expose.</summary>
    public Action<global::Microsoft.Playwright.BrowserTypeLaunchOptions>? ConfigureLaunchOptions { get; set; }
}
