using System.Runtime.InteropServices;
using WebReaper.Cli.Stealth;

namespace WebReaper.Cli.Commands;

/// <summary>
/// ADR-0055: <c>webreaper stealth install [&lt;backend&gt;]</c> — stealth-fork
/// acquisition. Walks <see cref="KnownStealthBackends"/>; interactive
/// picker by default; <c>--yes</c> for unattended; per-backend
/// <c>--version</c> pin. Downloads from each backend's official upstream
/// URL (legal model = <c>playwright install</c>).
/// </summary>
/// <remarks>
/// v1 ships <c>install</c>; <c>list</c>, <c>path</c>, <c>uninstall</c>
/// follow. The picker UI flips to non-interactive (or fails fast) when
/// <c>--yes</c> is set or <c>WEBREAPER_AUTO_STEALTH=1</c> in env.
/// </remarks>
internal static class StealthCommand
{
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
        // Optional second positional: the backend name to install. Falls
        // back to the interactive picker.
        var requested = args.Positional.Count > 1 ? args.Positional[1] : null;
        var unattended = args.HasFlag("yes") || EnvIsTrue("WEBREAPER_AUTO_STEALTH");

        StealthBackend? backend;
        if (requested is not null)
        {
            backend = KnownStealthBackends.Find(requested);
            if (backend is null)
            {
                Console.Error.WriteLine($"Unknown stealth backend: {requested}");
                Console.Error.WriteLine("Known backends:");
                foreach (var b in KnownStealthBackends.All)
                    Console.Error.WriteLine($"  • {b.Name}");
                return 2;
            }
        }
        else
        {
            if (unattended)
            {
                Console.Error.WriteLine(
                    "Unattended install requires a backend name: webreaper stealth install <name> --yes");
                return 2;
            }
            backend = PromptPicker();
            if (backend is null) return 1;
        }

        var version = args.GetFlag("version") ?? backend.RecommendedVersion;

        if (!unattended)
        {
            Console.WriteLine();
            Console.WriteLine($"  {backend.DisplayName} v{version}");
            Console.WriteLine($"  Size:    ~{backend.SizeMb} MB");
            Console.WriteLine($"  License: {backend.LicenseUrl}");
            Console.WriteLine();
            Console.Write($"By using {backend.DisplayName} you accept its binary license. Proceed? [Y/n] ");
            var reply = Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(reply) && !reply.Equals("y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Aborted.");
                return 1;
            }
        }

        var cacheDir = System.IO.Path.Combine(
            BrowserCommand.GetWebReaperHome(), "stealth", backend.Name, version);
        Directory.CreateDirectory(cacheDir);

        var rid = GetRid();
        var url = backend.ReleaseUrlPattern
            .Replace("{version}", version)
            .Replace("{rid}", rid);

        Console.WriteLine($"↓ Downloading {backend.DisplayName} v{version} from {url}");
        var archive = System.IO.Path.Combine(cacheDir, $"{backend.Name}-{rid}.tar.gz");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
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

        Console.WriteLine($"↓ Extracting to {cacheDir}");
        try { ExtractTar(archive, cacheDir); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"✗ Extract failed: {ex.Message}");
            return 1;
        }
        try { File.Delete(archive); } catch { }

        var binary = ExpectedBinaryPath(cacheDir, backend);
        if (!File.Exists(binary))
        {
            Console.Error.WriteLine(
                $"✗ Install completed but expected binary not found at {binary}. " +
                "The upstream archive layout may have changed; please report this.");
            return 1;
        }

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("chmod", $"+x \"{binary}\"")
                {
                    UseShellExecute = false, CreateNoWindow = true,
                };
                using var p = System.Diagnostics.Process.Start(psi);
                p?.WaitForExit();
            }
            catch { /* best-effort; user can chmod manually if needed */ }
        }

        Console.WriteLine($"✓ Installed: {binary}");
        return 0;
    }

    private static int Path(ParsedArgs args)
    {
        var name = args.Positional.Count > 1 ? args.Positional[1] : null;
        if (name is null) { Console.Error.WriteLine("Usage: webreaper stealth path <backend>"); return 2; }
        var backend = KnownStealthBackends.Find(name);
        if (backend is null) { Console.Error.WriteLine($"Unknown backend: {name}"); return 2; }
        var version = args.GetFlag("version") ?? backend.RecommendedVersion;
        var cacheDir = System.IO.Path.Combine(
            BrowserCommand.GetWebReaperHome(), "stealth", backend.Name, version);
        var binary = ExpectedBinaryPath(cacheDir, backend);
        if (!File.Exists(binary))
        {
            Console.Error.WriteLine($"{backend.DisplayName} v{version} not installed. Run: webreaper stealth install {backend.Name}");
            return 1;
        }
        Console.WriteLine(binary);
        return 0;
    }

    private static int List()
    {
        Console.WriteLine("Available stealth backends (curated; install with `webreaper stealth install <name>`):");
        foreach (var b in KnownStealthBackends.All)
            Console.WriteLine($"  • {b.Name,-15} {b.DisplayName,-15} v{b.RecommendedVersion}  ({b.SizeMb} MB)  {b.Description}");
        return 0;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("""
            Usage: webreaper stealth <subcommand>
              install [<backend>] [--version V] [--yes]
                                       Download from upstream; interactive picker
                                       by default; --yes (or WEBREAPER_AUTO_STEALTH=1)
                                       for unattended.
              path    <backend>        Print cached binary path
              list                     List available curated backends

            Curated backends are the ones the CLI can install. Library satellites
            (WebReaper.Stealth.X) ship freely; CLI integration is a small PR per backend.
            """);
        return 2;
    }

    private static StealthBackend? PromptPicker()
    {
        Console.WriteLine("Available stealth backends (downloaded from upstream; not bundled):");
        for (var i = 0; i < KnownStealthBackends.All.Length; i++)
        {
            var b = KnownStealthBackends.All[i];
            Console.WriteLine($"  [{i + 1}] {b.DisplayName,-15} v{b.RecommendedVersion}  ({b.SizeMb} MB)  {b.Description}");
        }
        Console.Write($"Choice [1]: ");
        var reply = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(reply)) return KnownStealthBackends.All[0];
        if (!int.TryParse(reply, out var n) || n < 1 || n > KnownStealthBackends.All.Length)
        {
            Console.Error.WriteLine($"Invalid choice '{reply}'.");
            return null;
        }
        return KnownStealthBackends.All[n - 1];
    }

    private static string ExpectedBinaryPath(string cacheDir, StealthBackend backend)
    {
        var name = OperatingSystem.IsWindows() ? backend.BinaryName + ".exe" : backend.BinaryName;
        return System.IO.Path.Combine(cacheDir, name);
    }

    private static void ExtractTar(string archive, string destDir)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("tar", $"-xzf \"{archive}\" -C \"{destDir}\"")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start `tar` — is it on PATH? (Windows 10+ ships tar.exe in-box.)");
        p.WaitForExit();
        if (p.ExitCode != 0)
        {
            var stderr = p.StandardError.ReadToEnd();
            throw new InvalidOperationException($"tar extract failed: {stderr.Trim()}");
        }
    }

    private static string GetRid()
    {
        if (OperatingSystem.IsWindows())
            return Environment.Is64BitProcess ? "win-x64" : "win-x86";
        if (OperatingSystem.IsMacOS())
            return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        if (OperatingSystem.IsLinux())
            return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
        throw new PlatformNotSupportedException();
    }

    private static bool EnvIsTrue(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
    }
}
