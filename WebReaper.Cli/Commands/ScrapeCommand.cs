using System.Diagnostics;
using WebReaper.Builders;
using WebReaper.Cdp;
using WebReaper.Cli.Stealth;
using WebReaper.Sinks.Models;

namespace WebReaper.Cli.Commands;

// `webreaper scrape <url>` — the funnel's primitive call. Markdown to
// stdout by default; JSON when --schema is supplied.
//
// ADR-0055 + ADR-0056 layered escalation:
//   1. BYO via --browser-cdp-url
//   2. --browser → managed Chromium spawn via WithCdpPageLoader(CdpLaunchOptions)
//   3. --browser + bot-check detected → Y/n prompt → inline stealth install
//      + single retry with the stealth binary as the executable
//
// The escalation is browser-mode only: a pure-HTTP fetch doesn't auto-promote
// to a browser via this path.
internal static class ScrapeCommand
{
    public static async Task<int> RunAsync(ParsedArgs args)
    {
        if (args.Positional.Count < 1)
            throw new CliException("Missing <url>. Usage: webreaper scrape <url> [flags]");

        var ctx = ParseContext(args);

        // First attempt: per ctx (BYO / managed / --stealth pre-installed).
        var first = await RunOnceAsync(ctx, stealthExecutablePath: null);

        // No escalation path on pure-HTTP scrapes.
        if (!ctx.Browser)
        {
            WriteEmptyHint(ctx, first.Records.Count);
            return await ShipAsync(ctx, first);
        }

        // ADR-0056: conservative bot-check detector. Runs over the last
        // page's HTML + record count. HTTP status is null on the browser
        // path today (named ADR-0056 follow-up).
        var verdict = BotCheckDetector.Detect(
            httpStatus: null,
            renderedHtml: first.LastHtml,
            recordCount: first.Records.Count);

        if (!verdict.LikelyBlocked)
        {
            WriteEmptyHint(ctx, first.Records.Count);
            return await ShipAsync(ctx, first);
        }

        // Block detected. Choose action per ctx.
        Console.Error.WriteLine($"⚠  {verdict.Reason}");

        if (ctx.NoAutoStealth)
        {
            // Opt-out: warn-only, no escalation. Pre-existing first-attempt
            // result is what we ship.
            Console.Error.WriteLine(
                "⚠  Auto-stealth disabled (--no-auto-stealth); retry manually with --stealth.");
            return await ShipAsync(ctx, first);
        }

        if (!ctx.AutoStealth)
        {
            // Interactive prompt — Y/n with default Y.
            Console.Error.Write(
                "?  Download CloakBrowser stealth backend (~220 MB) and retry? [Y/n] ");
            var reply = Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(reply) && !reply.Equals("y", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Aborted; shipping first-attempt result.");
                return await ShipAsync(ctx, first);
            }
        }

        // Inline install — subprocess `webreaper stealth install cloakbrowser --yes`.
        // Substitutability (ADR-0056): the same invocation the user could
        // run by hand.
        Console.Error.WriteLine("↓  Installing CloakBrowser via `webreaper stealth install cloakbrowser --yes`...");
        var installCode = await RunSelfAsync("stealth", "install", "cloakbrowser", "--yes");
        if (installCode != 0)
        {
            Console.Error.WriteLine($"✗  Stealth install failed (exit {installCode}); shipping first-attempt result.");
            await RecordOutput.WriteAsync(first.Records, ctx.Output);
            return installCode;
        }

        // Resolve the installed binary path.
        var stealthPath = await CaptureSelfAsync("stealth", "path", "cloakbrowser");
        if (stealthPath is null)
        {
            Console.Error.WriteLine("✗  Stealth install reported success but path resolution failed.");
            await RecordOutput.WriteAsync(first.Records, ctx.Output);
            return 1;
        }

        // Single retry — exactly one. A second bot-check verdict surfaces as
        // exit 1 + a captcha-solver pointer.
        Console.Error.WriteLine("↓  Retrying scrape against the stealth backend...");
        var retry = await RunOnceAsync(ctx, stealthExecutablePath: stealthPath);

        var retryVerdict = BotCheckDetector.Detect(
            null, retry.LastHtml, retry.Records.Count);
        if (retryVerdict.LikelyBlocked)
        {
            Console.Error.WriteLine(
                $"✗  Stealth retry still likely blocked: {retryVerdict.Reason}");
            Console.Error.WriteLine(
                "   The site may need a captcha-solver (deferred — see ADR-0055 §F5).");
            await RecordOutput.WriteAsync(retry.Records, ctx.Output);
            return 1;
        }

        return await ShipAsync(ctx, retry);
    }

    // ----- ship the records, block-aware exit code (ADR-0083) -----

    // Write the attempt's records to the configured output, then return the
    // exit code: non-zero when the core block detector flagged any page this run
    // (the load looked like a bot-check challenge), zero otherwise. Additive to
    // the ADR-0056 BotCheckDetector escalation — that heuristic decides whether
    // to *retry* with stealth; this tally decides the *exit code* of whatever
    // result we ship, so an unattended caller can detect a blocked scrape.
    private static async Task<int> ShipAsync(ScrapeContext ctx, AttemptResult attempt)
    {
        await RecordOutput.WriteAsync(attempt.Records, ctx.Output);
        if (attempt.BlockedPageCount > 0)
        {
            Console.Error.WriteLine(
                $"⚠  {attempt.BlockedPageCount} page(s) looked blocked; the site may use bot protection.");
            return 1;
        }
        return 0;
    }

    // ----- empty-result hint -----

    // Cheap-win companion to the (currently unreachable) bot-check escalation:
    // when a scrape returns no records, point the user at the next transport to
    // try instead of silently shipping empty stdout. The decision is the pure
    // EmptyResultAdvisor; this only writes the line to stderr.
    private static void WriteEmptyHint(ScrapeContext ctx, int recordCount)
    {
        var hint = EmptyResultAdvisor.Advise(ctx.Browser, ctx.Stealth, recordCount);
        if (hint is not null)
            Console.Error.WriteLine($"⚠  {hint}");
    }

    // ----- single scrape attempt -----

    // ADR-0083: BlockedPageCount carries the block detector's run-level tally
    // out of the engine so RunAsync can warn + exit non-zero on a blocked load.
    internal sealed record AttemptResult(List<ParsedData> Records, string? LastHtml, int BlockedPageCount);

    private static async Task<AttemptResult> RunOnceAsync(
        ScrapeContext ctx, string? stealthExecutablePath)
    {
        // ADR-0040: AsMarkdown is the default; Extract(schema) is the
        // upgrade. A future LLM extractor (ADR-0044) will land as a
        // third terminal (e.g. --as llm).
        var seed = ctx.Browser
            ? ScraperEngineBuilder.CrawlWithBrowser(ctx.Url)
            : ScraperEngineBuilder.Crawl(ctx.Url);

        var builder = ctx.SchemaPath is not null
            ? seed.Extract(SchemaFile.Load(ctx.SchemaPath))
            : seed.AsMarkdown();

        // ADR-0055 layered auto-spawn (paired with the ADR-0056 retry override):
        //   • If stealthExecutablePath is set → spawn it via launch-and-connect.
        //   • Else if --browser-cdp-url is set → connect-to-existing.
        //   • Else if --browser is set → spawn vanilla Chromium.
        if (stealthExecutablePath is not null)
        {
            builder = builder.WithCdpPageLoader(new CdpLaunchOptions
            {
                ExecutablePath = stealthExecutablePath,
                Headless = true,
            });
        }
        else if (ctx.CdpUrl is not null)
        {
            builder = builder.WithCdpPageLoader(ctx.CdpUrl);
        }
        else if (ctx.Browser)
        {
            builder = builder.WithCdpPageLoader(new CdpLaunchOptions
            {
                Headless = true,
            });
        }

        if (ctx.Follow is not null) builder = builder.Follow(ctx.Follow);
        if (ctx.MaxAge is { } age) builder = builder.WithMaxAge(age);

        var records = new List<ParsedData>();
        builder = builder.Subscribe(records.Add);
        builder = builder.StopWhenAllLinksProcessed();

        // ADR-0058: await using disposes the engine's resources on scope
        // exit — including any builder-time-spawned subprocess (managed
        // Chromium, stealth binary on retry). Per-attempt cleanup makes
        // the retry path correct (no two-Chromiums-running window).
        // ADR-0083: capture the RunReport so the block detector's tally
        // surfaces to the caller (warning + non-zero exit on a blocked load).
        string? lastHtml = null;
        var blockedPageCount = 0;
        await using (var engine = await builder.BuildAsync())
        {
            var report = await engine.RunAsync();
            blockedPageCount = report.BlockedPageCount;
        }

        // ADR-0056 detector input: approximate the rendered HTML from the
        // last record. ParsedData has no raw-HTML field; the Markdown
        // extractor (ADR-0040) emits {title, markdown} with the rendered
        // text in the markdown field — substring matching for challenge
        // markers works against either. v10.x ships this approximation;
        // a follow-up may add a raw-HTML peek-sink for higher fidelity.
        if (records.Count > 0)
        {
            lastHtml = records[^1].Data.ToJsonString();
        }

        return new AttemptResult(records, lastHtml, blockedPageCount);
    }

    // ----- self-invocation helpers -----

    /// <summary>Re-invoke this CLI binary as a subprocess and inherit
    /// stdout/stderr (so the install command's ↓/✓ progress lines render
    /// to the user). Returns the exit code.</summary>
    private static async Task<int> RunSelfAsync(params string[] argv)
    {
        var path = Environment.ProcessPath
            ?? throw new CliException("Cannot resolve own executable path for stealth subprocess.");
        var psi = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = false,
        };
        foreach (var a in argv) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)
            ?? throw new CliException($"Failed to spawn self ({path}).");
        await p.WaitForExitAsync();
        return p.ExitCode;
    }

    /// <summary>Re-invoke this binary and capture stdout. Returns the first
    /// non-empty line, or null if the subprocess failed or its stdout was
    /// empty.</summary>
    private static async Task<string?> CaptureSelfAsync(params string[] argv)
    {
        var path = Environment.ProcessPath;
        if (path is null) return null;

        var psi = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in argv) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi);
        if (p is null) return null;

        var stdout = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0) return null;

        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0) return trimmed;
        }
        return null;
    }

    // ----- arg parsing -----

    internal sealed record ScrapeContext(
        string Url,
        string? SchemaPath,
        string? Output,
        TimeSpan? MaxAge,
        string? Follow,
        string? CdpUrl,
        bool Browser,
        bool Stealth,
        bool AutoStealth,
        bool NoAutoStealth);

    internal static ScrapeContext ParseContext(ParsedArgs args)
    {
        var url = Urls.Normalize(args.Positional[0]);
        var cdpUrl = args.GetFlag("browser-cdp-url");
        var browser = args.HasFlag("browser") || cdpUrl is not null;
        var stealth = args.HasFlag("stealth");
        var autoStealthFlag = args.HasFlag("auto-stealth")
            || string.Equals(Environment.GetEnvironmentVariable("WEBREAPER_AUTO_STEALTH"), "1", StringComparison.Ordinal)
            || string.Equals(Environment.GetEnvironmentVariable("WEBREAPER_AUTO_STEALTH"), "true", StringComparison.OrdinalIgnoreCase);
        var noAutoStealth = args.HasFlag("no-auto-stealth");

        // --stealth without --browser implies --browser (the user opted into
        // a Chromium-based scrape; the only difference is which Chromium).
        if (stealth) browser = true;

        return new ScrapeContext(
            Url: url,
            SchemaPath: args.GetFlag("schema"),
            Output: args.GetFlag("output"),
            MaxAge: args.GetTimeSpanFlag("max-age"),
            Follow: args.GetFlag("follow"),
            CdpUrl: cdpUrl,
            Browser: browser,
            Stealth: stealth,
            AutoStealth: autoStealthFlag,
            NoAutoStealth: noAutoStealth);
    }

}
