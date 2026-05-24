using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using WebReaper.Builders;
using WebReaper.Cdp;
using WebReaper.Cli.Stealth;
using WebReaper.Domain.Parsing;
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
            await Emit(first.Records, ctx.Output);
            return 0;
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
            await Emit(first.Records, ctx.Output);
            return 0;
        }

        // Block detected. Choose action per ctx.
        Console.Error.WriteLine($"⚠  {verdict.Reason}");

        if (ctx.NoAutoStealth)
        {
            // Opt-out: warn-only, no escalation. Pre-existing first-attempt
            // result is what we ship.
            Console.Error.WriteLine(
                "⚠  Auto-stealth disabled (--no-auto-stealth); retry manually with --stealth.");
            await Emit(first.Records, ctx.Output);
            return 0;
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
                await Emit(first.Records, ctx.Output);
                return 0;
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
            await Emit(first.Records, ctx.Output);
            return installCode;
        }

        // Resolve the installed binary path.
        var stealthPath = await CaptureSelfAsync("stealth", "path", "cloakbrowser");
        if (stealthPath is null)
        {
            Console.Error.WriteLine("✗  Stealth install reported success but path resolution failed.");
            await Emit(first.Records, ctx.Output);
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
            await Emit(retry.Records, ctx.Output);
            return 1;
        }

        await Emit(retry.Records, ctx.Output);
        return 0;
    }

    // ----- single scrape attempt -----

    internal sealed record AttemptResult(List<ParsedData> Records, string? LastHtml);

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
            ? seed.Extract(LoadSchema(ctx.SchemaPath))
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
        string? lastHtml = null;
        await using (var engine = await builder.BuildAsync())
        {
            await engine.RunAsync();
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

        return new AttemptResult(records, lastHtml);
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
        var url = args.Positional[0];
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

    // ----- emission + schema parsing -----

    private static async Task Emit(List<ParsedData> records, string? output)
    {
        // Default: write to stdout. With --output, write to file.
        // Single record → output its Data JSON; multiple → JSON Lines.
        var sb = new System.Text.StringBuilder();
        foreach (var r in records)
        {
            sb.Append(r.Data.ToJsonString());
            sb.Append('\n');
        }

        var text = sb.ToString().TrimEnd('\n');

        if (output is not null)
            await File.WriteAllTextAsync(output, text);
        else
            Console.WriteLine(text);
    }

    private static Schema LoadSchema(string path)
    {
        if (!File.Exists(path))
            throw new CliException($"Schema file not found: {path}");

        string content;
        try { content = File.ReadAllText(path); }
        catch (Exception ex)
        {
            throw new CliException($"Failed to read schema file '{path}': {ex.Message}");
        }

        JsonNode? root;
        try { root = JsonNode.Parse(content); }
        catch (JsonException ex)
        {
            throw new CliException($"Schema file '{path}' is not valid JSON: {ex.Message}");
        }

        if (root is not JsonObject obj)
            throw new CliException(
                $"Schema file '{path}' must contain a JSON object at the root.");

        var schema = BuildSchema(obj);
        return schema;
    }

    private static Schema BuildSchema(JsonObject obj)
    {
        // Recursive parse of the schema JSON shape into the library's
        // Schema/SchemaElement records. The shape pinned in ADR-0043:
        //   { field, selector?, type?, attr?, is_list?, children? }
        //
        // An object with children is a Schema (a nested container);
        // a leaf with no children is a SchemaElement.

        var children = obj["children"] as JsonArray;

        if (children is null || children.Count == 0)
        {
            // Leaf.
            return WrapAsSchema(BuildElement(obj));
        }

        // Container.
        var field = obj["field"]?.GetValue<string>();
        var selector = obj["selector"]?.GetValue<string>();
        var isList = obj["is_list"]?.GetValue<bool>() ?? false;

        var container = field is not null
            ? new Schema(field) { Selector = selector ?? string.Empty, IsList = isList }
            : new Schema();

        foreach (var child in children)
        {
            if (child is not JsonObject childObj)
                throw new CliException("Schema children must be objects.");
            container.Add(BuildElement(childObj));
        }

        return container;
    }

    private static SchemaElement BuildElement(JsonObject obj)
    {
        var field = obj["field"]?.GetValue<string>()
            ?? throw new CliException("Schema element is missing 'field'.");

        var children = obj["children"] as JsonArray;
        if (children is not null && children.Count > 0)
        {
            return BuildSchema(obj);
        }

        var selector = obj["selector"]?.GetValue<string>() ?? string.Empty;
        var attr = obj["attr"]?.GetValue<string>();
        var isList = obj["is_list"]?.GetValue<bool>() ?? false;
        var type = ParseDataType(obj["type"]?.GetValue<string>());

        var element = new SchemaElement(field, selector)
        {
            Type = type,
            IsList = isList
        };

        if (attr is not null) element.Attr = attr;

        return element;
    }

    private static Schema WrapAsSchema(SchemaElement element)
    {
        if (element is Schema schema) return schema;
        return new Schema { element };
    }

    private static DataType? ParseDataType(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        return raw.ToLowerInvariant() switch
        {
            "string" => DataType.String,
            "integer" or "int" => DataType.Integer,
            "float" or "double" or "decimal" => DataType.Float,
            "boolean" or "bool" => DataType.Boolean,
            "datetime" or "date" => DataType.DataTime,
            _ => throw new CliException(
                $"Unknown schema type '{raw}'. Valid: string, integer, float, boolean, datetime.")
        };
    }
}
