using System.Runtime.InteropServices;

namespace WebReaper.Cli.Commands;

/// <summary>
/// ADR-0055: <c>webreaper browser install</c> — vanilla Chromium
/// acquisition. Downloads from Microsoft's Playwright CDN (the same CDN
/// <c>playwright install</c> uses; high-availability, signed builds with
/// checksums). Caches under <c>~/.webreaper/browsers/chromium-{revision}/</c>.
/// </summary>
/// <remarks>
/// v1 ships <c>install</c>. <c>list</c>, <c>path</c>, <c>uninstall</c>
/// follow in a v1.1 minor — surface designed for them in the ADR. The
/// install command is the foundational one: it gates the
/// auto-spawn-managed rung of the CLI's layered browser-detection.
/// </remarks>
internal static class BrowserCommand
{
    private const string DefaultRevision = "1148"; // a pinned Playwright-CDN Chromium build

    public static async Task<int> RunAsync(ParsedArgs args)
    {
        var sub = args.Positional.Count > 0 ? args.Positional[0] : null;

        return sub switch
        {
            "install" => await InstallAsync(args),
            "path" => Path(args),
            "list" => List(),
            _ => Usage(),
        };
    }

    private static async Task<int> InstallAsync(ParsedArgs args)
    {
        var revision = args.GetFlag("revision") ?? DefaultRevision;
        var cacheDir = System.IO.Path.Combine(GetWebReaperHome(), "browsers", $"chromium-{revision}");
        var binary = ExpectedChromiumBinary(cacheDir);

        if (File.Exists(binary))
        {
            Console.WriteLine($"✓ Chromium {revision} already installed: {binary}");
            return 0;
        }

        Directory.CreateDirectory(cacheDir);
        var rid = GetPlaywrightRid();
        var url = $"https://playwright.azureedge.net/builds/chromium/{revision}/chromium-{rid}.zip";
        var archive = System.IO.Path.Combine(cacheDir, $"chromium-{rid}.zip");

        Console.WriteLine($"↓ Downloading Chromium {revision} ({rid}) from Microsoft Playwright CDN...");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(archive);
            await resp.Content.CopyToAsync(fs);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"✗ Download failed: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"↓ Extracting to {cacheDir}...");
        try
        {
            System.IO.Compression.ZipFile.ExtractToDirectory(archive, cacheDir, overwriteFiles: true);
            try { File.Delete(archive); } catch { }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"✗ Extract failed: {ex.Message}");
            return 1;
        }

        if (!File.Exists(binary))
        {
            Console.Error.WriteLine(
                $"✗ Install completed but expected binary not found at {binary}. " +
                "The Playwright CDN layout may have changed; please report this.");
            return 1;
        }

        Console.WriteLine($"✓ Installed: {binary}");
        return 0;
    }

    private static int Path(ParsedArgs args)
    {
        var revision = args.GetFlag("revision") ?? DefaultRevision;
        var cacheDir = System.IO.Path.Combine(GetWebReaperHome(), "browsers", $"chromium-{revision}");
        var binary = ExpectedChromiumBinary(cacheDir);
        if (File.Exists(binary))
        {
            Console.WriteLine(binary);
            return 0;
        }
        Console.Error.WriteLine($"Chromium {revision} not installed. Run: webreaper browser install");
        return 1;
    }

    private static int List()
    {
        var browsersDir = System.IO.Path.Combine(GetWebReaperHome(), "browsers");
        if (!Directory.Exists(browsersDir))
        {
            Console.WriteLine("(no browsers installed)");
            return 0;
        }
        var dirs = Directory.GetDirectories(browsersDir);
        if (dirs.Length == 0)
        {
            Console.WriteLine("(no browsers installed)");
            return 0;
        }
        foreach (var d in dirs.OrderBy(d => d))
            Console.WriteLine(System.IO.Path.GetFileName(d));
        return 0;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("""
            Usage: webreaper browser <subcommand>
              install [--revision N]   Download managed Chromium (default: latest known build)
              path    [--revision N]   Print cached binary path
              list                     List installed cached versions
            """);
        return 2;
    }

    internal static string ExpectedChromiumBinary(string cacheDir)
    {
        // Layout of the Playwright CDN zip is `chrome-{rid}/chrome` (or `.exe`).
        var rid = GetPlaywrightRid();
        var subdir = System.IO.Path.Combine(cacheDir, $"chrome-{rid}");
        return OperatingSystem.IsWindows()
            ? System.IO.Path.Combine(subdir, "chrome.exe")
            : OperatingSystem.IsMacOS()
                ? System.IO.Path.Combine(subdir, "Chromium.app", "Contents", "MacOS", "Chromium")
                : System.IO.Path.Combine(subdir, "chrome");
    }

    internal static string GetWebReaperHome()
    {
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return System.IO.Path.Combine(local, "WebReaper");
        }
        var home = Environment.GetEnvironmentVariable("HOME") ?? System.IO.Path.GetTempPath();
        return System.IO.Path.Combine(home, ".webreaper");
    }

    internal static string GetPlaywrightRid()
    {
        if (OperatingSystem.IsWindows()) return "win64";
        if (OperatingSystem.IsMacOS())
            return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "mac-arm64" : "mac";
        return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux";
    }
}
