using WebReaper.Builders;
using WebReaper.Cdp;
using WebReaper.Sinks.Models;

namespace WebReaper.Cli.Commands;

// `webreaper crawl <url>`: the ADR-0081 whole-site recursive Site sweep. From
// the start URL (seeded by the sitemap too, unless --no-sitemap), follow every
// on-domain link breadth-first, extract each page, and stream JSON Lines to
// stdout (or --output). Markdown by default; JSON when --schema is given. The
// Visited-link tracker dedups and terminates the sweep; --max-pages bounds it.
//
// ADR-0083: the sweep starts at the HTTP rung and the escalating loader
// auto-climbs a blocked page to the browser rung (lazy — a clean crawl never
// spawns a browser; host-stickiness bounds the cost on a protected domain). The
// browser rung is registered only when a Chromium is actually present, so a
// browser-less host stays HTTP-only. Stealth is opt-in via `scrape --stealth`.
internal static class CrawlCommand
{
    private static readonly string[] ChromeNames =
        ["google-chrome", "chromium", "chrome", "microsoft-edge", "msedge"];


    public static async Task<int> RunAsync(ParsedArgs args)
    {
        if (args.Positional.Count < 1)
            throw new CliException("Missing <url>. Usage: webreaper crawl <url> [flags]");

        var ctx = ParseContext(args);

        // ADR-0081: AsMarkdown is the default sweep extraction (every page
        // becomes clean content, sidestepping page heterogeneity); --schema
        // applies the deterministic fold to every page instead.
        var seed = ScraperEngineBuilder.Crawl(ctx.Url);
        var builder = ctx.SchemaPath is not null
            ? seed.Extract(SchemaFile.Load(ctx.SchemaPath))
            : seed.AsMarkdown();

        builder = builder
            .Sweep(new SweepOptions
            {
                IncludeSubdomains = ctx.IncludeSubdomains,
                MaxDepth = ctx.MaxDepth,
                Sitemap = ctx.Sitemap,
            })
            .PageCrawlLimit(ctx.MaxPages)        // ADR-0032 cutoff
            .StopWhenAllLinksProcessed();        // terminate when the frontier saturates

        // ADR-0083: register the vanilla browser rung (managed Chromium spawn,
        // lazy) so a blocked page climbs HTTP -> browser. Only when a browser is
        // present, so a browser-less host degrades to HTTP-only instead of a
        // launch error mid-sweep.
        if (CdpLaunchHelpers.FindOnPath(ChromeNames) is not null)
            builder = builder.WithCdpPageLoader(new CdpLaunchOptions { Headless = true });

        // The crawl loop is parallel (ADR-0022), so sink emits are concurrent;
        // guard the collection. The default --max-pages bounds the memory.
        var records = new List<ParsedData>();
        builder = builder.Subscribe(r => { lock (records) records.Add(r); });

        // ADR-0058: await using disposes the engine's resources on scope exit.
        await using (var engine = await builder.BuildAsync())
        {
            await engine.RunAsync();
        }

        await RecordOutput.WriteAsync(records, ctx.Output);
        return 0;
    }

    internal sealed record CrawlContext(
        string Url,
        string? SchemaPath,
        string? Output,
        int MaxPages,
        int? MaxDepth,
        bool IncludeSubdomains,
        bool Sitemap);

    internal static CrawlContext ParseContext(ParsedArgs args)
    {
        // --max-depth absent ⇒ unbounded (null); present ⇒ parsed (a
        // non-integer value throws via GetIntFlag).
        int? maxDepth = args.HasFlag("max-depth")
            ? args.GetIntFlag("max-depth", int.MaxValue)
            : null;

        return new CrawlContext(
            Url: Urls.Normalize(args.Positional[0]),
            SchemaPath: args.GetFlag("schema"),
            Output: args.GetFlag("output"),
            MaxPages: args.GetIntFlag("max-pages", 1000),
            MaxDepth: maxDepth,
            IncludeSubdomains: args.HasFlag("include-subdomains"),
            Sitemap: !args.HasFlag("no-sitemap"));
    }
}
