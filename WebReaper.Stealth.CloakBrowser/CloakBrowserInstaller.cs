using Microsoft.Extensions.Logging;
using WebReaper.Cdp;

namespace WebReaper.Stealth.CloakBrowser;

/// <summary>
/// CloakBrowser binary acquisition (ADR-0054). Detect first, download from
/// upstream second, log license-acknowledgment on first download. Mirrors
/// <c>playwright install</c>'s on-user-request shape — the binary is
/// fetched from CloakHQ's own servers on the user's machine; nothing
/// rehosted.
/// </summary>
/// <remarks>
/// v1 ships the detection + download mechanics. Checksum verification is a
/// TODO (the release-manifest API exposes it; needs a follow-up to read
/// and verify). Resumable download is also a TODO; v1 retries from scratch
/// on partial-file failure.
/// </remarks>
public static class CloakBrowserInstaller
{
    /// <summary>The version v1 of this satellite installs unless the
    /// consumer overrides via <see cref="CloakBrowserOptions.Version"/>.</summary>
    public const string DefaultVersion = "0.3.30";

    /// <summary>The license URL surfaced on first install.</summary>
    public const string LicenseUrl = "https://github.com/CloakHQ/CloakBrowser/blob/main/BINARY-LICENSE.md";

    /// <summary>Idempotent: returns the path to a usable CloakBrowser
    /// executable for the current RID. Detects an existing install
    /// (PATH + the satellite's cache dir <c>~/.webreaper/stealth/cloakbrowser/</c>)
    /// first; downloads from upstream if absent.</summary>
    public static async Task<string> EnsureInstalledAsync(
        CloakBrowserOptions options,
        ILogger logger,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        if (!string.IsNullOrWhiteSpace(options.ExecutablePath))
        {
            if (!File.Exists(options.ExecutablePath))
                throw new FileNotFoundException(
                    $"CloakBrowserOptions.ExecutablePath does not exist: {options.ExecutablePath}");
            return options.ExecutablePath;
        }

        var version = options.Version ?? DefaultVersion;
        var cacheDir = Path.Combine(GetWebReaperHome(), "stealth", "cloakbrowser", version);
        var cachedBinary = ExpectedBinaryPath(cacheDir);
        if (File.Exists(cachedBinary)) return cachedBinary;

        // Try PATH detection — some users may have CloakBrowser installed
        // system-wide (e.g. via the vendor's own installer).
        var onPath = CdpLaunchHelpers.FindOnPath("cloakbrowser", "cloak-browser");
        if (onPath is not null)
        {
            logger.LogInformation("CloakBrowser: found pre-installed binary on PATH ({Path}); skipping download.", onPath);
            return onPath;
        }

        if (options.AutoInstall == AutoInstallPolicy.Disabled)
        {
            throw new InvalidOperationException(
                $"CloakBrowser binary not found and AutoInstall is Disabled. " +
                $"Either install CloakBrowser yourself and place the binary on PATH, " +
                $"or set CloakBrowserOptions.ExecutablePath, or change AutoInstall to PromptLogger / NoPromptYes.");
        }

        if (options.AutoInstall == AutoInstallPolicy.PromptLogger)
        {
            logger.LogWarning(
                "CloakBrowser: downloading binary v{Version} from upstream (~220 MB). " +
                "By using CloakBrowser you accept its binary license: {LicenseUrl}",
                version, LicenseUrl);
        }

        Directory.CreateDirectory(cacheDir);
        await DownloadAndExtractAsync(version, cacheDir, logger, ct);

        if (!File.Exists(cachedBinary))
            throw new InvalidOperationException(
                $"CloakBrowser install completed but expected binary not found at {cachedBinary}.");

        // On Unix, mark executable.
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                var fi = new FileInfo(cachedBinary);
                // chmod +x via P/Invoke would be cleanest; for simplicity
                // shell out to chmod. Cheap on the install hot path.
                var psi = new System.Diagnostics.ProcessStartInfo("chmod", $"+x \"{cachedBinary}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = System.Diagnostics.Process.Start(psi);
                await (p?.WaitForExitAsync(ct) ?? Task.CompletedTask);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "CloakBrowser: failed to chmod +x {Path}; you may need to do it manually.", cachedBinary);
            }
        }

        return cachedBinary;
    }

    /// <summary>The OS-conventional WebReaper home dir
    /// (<c>~/.webreaper/</c> on Unix; <c>%LOCALAPPDATA%/WebReaper/</c> on
    /// Windows). Shared with <see cref="WebReaper.Cdp"/>'s managed-browser
    /// cache.</summary>
    public static string GetWebReaperHome()
    {
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "WebReaper");
        }
        var home = Environment.GetEnvironmentVariable("HOME") ?? Path.GetTempPath();
        return Path.Combine(home, ".webreaper");
    }

    private static string ExpectedBinaryPath(string cacheDir)
    {
        // CloakBrowser's release archives unpack to a folder containing the
        // executable. Convention used by v1: the exe is at the root of the
        // cache dir, named `cloakbrowser` (or `cloakbrowser.exe` on Windows).
        // Future versions may need a per-version layout lookup.
        var name = OperatingSystem.IsWindows() ? "cloakbrowser.exe" : "cloakbrowser";
        return Path.Combine(cacheDir, name);
    }

    private static async Task DownloadAndExtractAsync(string version, string cacheDir, ILogger logger, CancellationToken ct)
    {
        var rid = GetRid();
        // v1: URL pattern based on the upstream's GitHub releases convention.
        // Real version of this code reads /releases/{tag} for the exact asset name + checksum.
        var assetName = $"cloakbrowser-{rid}.tar.gz";
        var url = $"https://github.com/CloakHQ/CloakBrowser/releases/download/v{version}/{assetName}";
        var archivePath = Path.Combine(cacheDir, assetName);

        logger.LogInformation("CloakBrowser: downloading {Url}", url);
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) })
        using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(archivePath);
            await resp.Content.CopyToAsync(fs, ct);
        }

        logger.LogInformation("CloakBrowser: extracting {Archive}", archivePath);
        // v1: relies on `tar` being on PATH. On Windows 10+, tar.exe ships in-box.
        var psi = new System.Diagnostics.ProcessStartInfo("tar", $"-xzf \"{archivePath}\" -C \"{cacheDir}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start `tar` — is it on PATH?");
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0)
        {
            var stderr = await p.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"tar extract failed: {stderr.Trim()}");
        }

        try { File.Delete(archivePath); } catch { /* best-effort */ }
    }

    private static string GetRid()
    {
        if (OperatingSystem.IsWindows())
            return Environment.Is64BitProcess ? "win-x64" : "win-x86";
        if (OperatingSystem.IsMacOS())
            return System.Runtime.InteropServices.RuntimeInformation.OSArchitecture
                == System.Runtime.InteropServices.Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        if (OperatingSystem.IsLinux())
            return System.Runtime.InteropServices.RuntimeInformation.OSArchitecture
                == System.Runtime.InteropServices.Architecture.Arm64 ? "linux-arm64" : "linux-x64";
        throw new PlatformNotSupportedException("Unsupported platform for CloakBrowser.");
    }
}
