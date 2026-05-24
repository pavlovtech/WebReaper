using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Builders;
using WebReaper.Cdp;

namespace WebReaper.Stealth.CloakBrowser;

/// <summary>
/// ADR-0054 stealth-backend convention: a satellite ships one extension
/// method on <see cref="ScraperEngineBuilder"/> wiring its fork's launcher
/// + installer to <c>WithCdpPageLoader(string)</c>. CloakBrowser is the
/// first concrete satellite.
/// </summary>
public static class CloakBrowserBuilderExtensions
{
    /// <summary>
    /// Use CloakBrowser as the Dynamic-page transport. On first call:
    /// detects an existing CloakBrowser binary (PATH + the satellite's
    /// cache dir); downloads from upstream if absent (subject to
    /// <see cref="CloakBrowserOptions.AutoInstall"/>); launches it with the
    /// stealth-fork's recommended flags; wires the resulting CDP endpoint
    /// into <see cref="CdpPageLoaderBuilderExtensions.WithCdpPageLoader(ScraperEngineBuilder, string)"/>.
    /// </summary>
    /// <remarks>
    /// Per ADR-0054: by using this satellite you accept CloakBrowser's
    /// binary license — see
    /// https://github.com/CloakHQ/CloakBrowser/blob/main/BINARY-LICENSE.md.
    /// On first install, the satellite logs a one-line acknowledgment via
    /// the registered <see cref="ILogger"/>. For unattended scenarios,
    /// set <see cref="CloakBrowserOptions.AutoInstall"/> to
    /// <see cref="AutoInstallPolicy.NoPromptYes"/>.
    /// </remarks>
    public static ScraperEngineBuilder WithCloakBrowser(
        this ScraperEngineBuilder builder,
        CloakBrowserOptions? options = null)
    {
        var opts = options ?? new CloakBrowserOptions();

        // Sync-over-async at the builder boundary, matching the rest of
        // the builder surface. Detection is cheap (one File.Exists); the
        // download is the slow path and runs on first use, not every call.
        // ILogger is supplied by the builder pipeline at LoadAsync time,
        // not at registration time — install + launch run at build time
        // here, so we use NullLogger. The transport's per-page work picks
        // up the real ILogger via the 4-arg factory.
        ILogger logger = NullLogger.Instance;
        var binaryPath = CloakBrowserInstaller.EnsureInstalledAsync(opts, logger, CancellationToken.None)
            .GetAwaiter().GetResult();
        var endpoint = CloakBrowserLauncher.LaunchAsync(binaryPath, opts, logger, CancellationToken.None)
            .GetAwaiter().GetResult();

        // Endpoint disposal is wired through the CDP transport's lifecycle —
        // when the transport disposes, the BYO CDP URL it connected to is
        // not torn down. We need a teardown hook. v1 leaves the spawned
        // CloakBrowser process running until process exit; the OS reaps
        // it. v2 (after ADR-0033 IAsyncDisposable seams reach into the
        // builder) will register the endpoint for disposal.
        return builder.WithCdpPageLoader(endpoint.CdpUrl);
    }
}
