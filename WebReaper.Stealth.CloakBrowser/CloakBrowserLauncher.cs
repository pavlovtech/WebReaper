using Microsoft.Extensions.Logging;
using WebReaper.Cdp;

namespace WebReaper.Stealth.CloakBrowser;

/// <summary>
/// CloakBrowser process launcher (ADR-0054). Wraps
/// <see cref="CdpLaunchHelpers.LaunchAsync"/> with the CloakBrowser-specific
/// recommended flags. The launched process is owned by the returned
/// <see cref="LaunchedCdpEndpoint"/>; <c>DisposeAsync</c> tears it down.
/// </summary>
public static class CloakBrowserLauncher
{
    /// <summary>The launch-flag set CloakBrowser's vendor recommends.
    /// Mostly mirrors a hardened Chromium config — no first-run dialogs,
    /// no automatic translation prompts, no background networking. The
    /// stealth patches live in the binary itself; the args don't add
    /// stealth, only sanity.</summary>
    public static readonly IReadOnlyList<string> RecommendedArgs =
    [
        "--no-first-run",
        "--no-default-browser-check",
        "--disable-background-networking",
        "--disable-background-timer-throttling",
        "--disable-renderer-backgrounding",
        "--disable-features=TranslateUI,Translate",
        "--disable-dev-shm-usage",
    ];

    /// <summary>Launch CloakBrowser at <paramref name="binaryPath"/> with
    /// the supplied <paramref name="options"/>; return a
    /// <see cref="LaunchedCdpEndpoint"/> the consumer hands to
    /// <c>WithCdpPageLoader(string)</c>.</summary>
    public static Task<LaunchedCdpEndpoint> LaunchAsync(
        string binaryPath,
        CloakBrowserOptions options,
        ILogger logger,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binaryPath);
        ArgumentNullException.ThrowIfNull(options);

        var args = new List<string>(RecommendedArgs);
        if (options.Headless && !args.Any(a => a.StartsWith("--headless", StringComparison.Ordinal)))
            args.Add("--headless=new");

        logger.LogInformation("CloakBrowser: launching {Binary} (headless={Headless})", binaryPath, options.Headless);
        return CdpLaunchHelpers.LaunchAsync(new CdpLaunchSpec(binaryPath, args), ct);
    }
}
