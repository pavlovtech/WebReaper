using System.Diagnostics;
using System.Text.Json.Nodes;

namespace WebReaper.Cdp;

/// <summary>
/// Public static utility for launching and probing CDP-capable browsers
/// (ADR-0052). The shared layer every <c>WebReaper.Stealth.X</c> satellite
/// composes on: the satellite spawns its fork's binary via
/// <see cref="LaunchAsync"/>, gets the resulting CDP URL, then hands it to
/// <c>WithCdpPageLoader(string)</c>.
/// </summary>
public static class CdpLaunchHelpers
{
    /// <summary>Find a browser executable by candidate names across PATH and
    /// platform-conventional install locations. Returns <c>null</c> if none
    /// matched. Names are tried in order; the first hit wins.</summary>
    public static string? FindOnPath(params string[] candidateNames)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var pathDirs = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var exeSuffix = OperatingSystem.IsWindows() ? ".exe" : "";

        foreach (var name in candidateNames)
        {
            var nameWithExt = name.EndsWith(exeSuffix, StringComparison.OrdinalIgnoreCase) ? name : name + exeSuffix;
            foreach (var dir in pathDirs)
            {
                try
                {
                    var candidate = Path.Combine(dir, nameWithExt);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { /* skip unreadable PATH entries */ }
            }

            foreach (var conv in ConventionalInstallPaths(name))
            {
                if (File.Exists(conv)) return conv;
            }
        }

        return null;
    }

    private static IEnumerable<string> ConventionalInstallPaths(string name)
    {
        if (OperatingSystem.IsMacOS())
        {
            yield return name.ToLowerInvariant() switch
            {
                "google-chrome" or "chrome" => "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
                "chromium" => "/Applications/Chromium.app/Contents/MacOS/Chromium",
                "microsoft-edge" or "msedge" => "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
                _ => "",
            };
        }
        else if (OperatingSystem.IsWindows())
        {
            var pf = Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files";
            var pfx86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? @"C:\Program Files (x86)";
            switch (name.ToLowerInvariant())
            {
                case "google-chrome" or "chrome":
                    yield return Path.Combine(pf, "Google", "Chrome", "Application", "chrome.exe");
                    yield return Path.Combine(pfx86, "Google", "Chrome", "Application", "chrome.exe");
                    break;
                case "microsoft-edge" or "msedge":
                    yield return Path.Combine(pf, "Microsoft", "Edge", "Application", "msedge.exe");
                    yield return Path.Combine(pfx86, "Microsoft", "Edge", "Application", "msedge.exe");
                    break;
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            // PATH dirs cover the common Linux installs; nothing extra
            // beyond /usr/bin/<name> which PATH already includes.
        }
    }

    /// <summary>Spawn a CDP-capable browser with
    /// <c>--remote-debugging-port=0</c> (ephemeral port), wait for the
    /// endpoint to publish, return the resolved WebSocket URL plus a
    /// teardown handle.</summary>
    public static async Task<LaunchedCdpEndpoint> LaunchAsync(CdpLaunchSpec spec, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (!File.Exists(spec.ExecutablePath))
            throw new FileNotFoundException($"Browser executable not found: {spec.ExecutablePath}");

        var userDataDir = spec.UserDataDir;
        var disposeTempUserDataDir = false;
        if (string.IsNullOrEmpty(userDataDir))
        {
            userDataDir = Path.Combine(Path.GetTempPath(), "webreaper-cdp-" + Guid.NewGuid().ToString("N")[..12]);
            Directory.CreateDirectory(userDataDir);
            disposeTempUserDataDir = true;
        }

        var args = new List<string>(spec.Args);
        if (!args.Any(a => a.StartsWith("--remote-debugging-port=", StringComparison.Ordinal)))
            args.Add("--remote-debugging-port=0");
        if (!args.Any(a => a.StartsWith("--user-data-dir=", StringComparison.Ordinal)))
            args.Add($"--user-data-dir={userDataDir}");

        var psi = new ProcessStartInfo
        {
            FileName = spec.ExecutablePath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to launch {spec.ExecutablePath}");

        // CDP-capable browsers print the WebSocket URL to STDERR on startup,
        // e.g. "DevTools listening on ws://127.0.0.1:54321/devtools/browser/<id>"
        var timeout = spec.StartupTimeout ?? TimeSpan.FromSeconds(30);
        var deadline = DateTime.UtcNow + timeout;
        string? cdpUrl = null;

        var stderrTask = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
                {
                    var line = await process.StandardError.ReadLineAsync(ct);
                    if (line == null) break;
                    var idx = line.IndexOf("ws://", StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        var url = line[idx..].Trim();
                        Volatile.Write(ref cdpUrl, url);
                        return;
                    }
                }
            }
            catch (OperationCanceledException) { }
        }, ct);

        while (Volatile.Read(ref cdpUrl) is null && !process.HasExited && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, ct);
        }

        var resolved = Volatile.Read(ref cdpUrl);
        if (resolved is null)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            if (disposeTempUserDataDir) TryRemoveDir(userDataDir);
            throw new TimeoutException(
                $"Timed out waiting {timeout.TotalSeconds:F1}s for CDP endpoint from {spec.ExecutablePath}. " +
                "Confirm the binary supports --remote-debugging-port; check stderr.");
        }

        return new LaunchedCdpEndpoint(resolved, process.Id, async () =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { await process.WaitForExitAsync(); } catch { }
            process.Dispose();
            if (disposeTempUserDataDir) TryRemoveDir(userDataDir);
        });
    }

    /// <summary>Probe a CDP endpoint by checking the well-known
    /// <c>/json/version</c> HTTP endpoint a CDP-capable browser exposes
    /// alongside its WebSocket. Returns true when the endpoint responds
    /// with a CDP-shaped JSON payload within <paramref name="timeout"/>.</summary>
    public static async Task<bool> ProbeAsync(string cdpUrl, TimeSpan timeout, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cdpUrl)) return false;

        try
        {
            // ws://host:port/... → http://host:port/json/version
            var uri = new Uri(cdpUrl);
            var httpUrl = $"http://{uri.Host}:{uri.Port}/json/version";

            using var http = new HttpClient { Timeout = timeout };
            using var resp = await http.GetAsync(httpUrl, ct);
            if (!resp.IsSuccessStatusCode) return false;

            var body = await resp.Content.ReadAsStringAsync(ct);
            var parsed = JsonNode.Parse(body) as JsonObject;
            return parsed?["webSocketDebuggerUrl"] is not null;
        }
        catch
        {
            return false;
        }
    }

    private static void TryRemoveDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
    }
}
