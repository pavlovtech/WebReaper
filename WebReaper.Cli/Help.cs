namespace WebReaper.Cli;

// ADR-0043: the hand-rolled --help formatter. Reads exactly the way
// firecrawl's CLI help reads — one paragraph per command, a flat list
// of flags, no syntax-tree dump.
internal static class Help
{
    public static readonly string Top = @"
webreaper — declarative .NET web scraper / crawler, AI-native.

Usage:
  webreaper <command> [args] [flags]

Commands:
  scrape <url>          Fetch a URL; output Markdown by default,
                        or JSON if --schema is given.
  map <url>             Discover URLs (sitemap.xml + root-page links).
  browser <sub>         Manage the managed Chromium for --browser scrapes
                        (install/path/list).
  stealth <sub>         Manage stealth Chromium forks (CloakBrowser, etc.)
                        for protected sites (install/path/list).
  init                  Write the WebReaper Agent Skill into your
                        coding-agent configuration.
  version               Print the version.
  help                  Print this message.

Flags (per-command):

  scrape:
    --schema <path>     JSON schema file (switches output to JSON).
    --output <path>     Write to a file instead of stdout.
    --max-age <dur>     Cache fetched pages for this long (30s/5m/2h/1d
                        or a TimeSpan).
    --browser           Use the headless-browser transport. Layered
                        auto-spawn (ADR-0055): BYO via --browser-cdp-url,
                        else managed Chromium from `webreaper browser install`.
    --browser-cdp-url   Connect to an existing CDP endpoint (e.g.
                        http://localhost:9222) — used for stealth backends
                        and BYO browser farms.
    --follow <selector> Follow links matching this CSS selector before
                        extraction (the Crawl chain's first step).
    --stealth           Skip the vanilla-browser attempt; go straight to
                        the stealth backend. Implies --browser.
    --auto-stealth      Bypass the Y/n prompt when a bot-check is detected
                        (CI / unattended). Equivalent to env
                        WEBREAPER_AUTO_STEALTH=1.
    --no-auto-stealth   Warn-only on bot-check detection; never install or
                        retry (ADR-0056 escape hatch).

  browser:
    install [--revision N]   Download managed Chromium to ~/.webreaper/.
    path    [--revision N]   Print cached binary path.
    list                     List installed cached versions.

  stealth:
    install [<backend>] [--version V] [--yes]
                             Download a stealth Chromium fork (CloakBrowser etc.).
                             Interactive picker by default; --yes for unattended.
    path    <backend>        Print cached binary path.
    list                     List curated stealth backends.

  map:
    --search <text>     Substring filter on the returned URLs
                        (case-insensitive).
    --max-urls <n>      Cap the result count (default 1000).
    --allow-offsite     Keep URLs whose host differs from <url>.
    --no-sitemap        Skip robots.txt / sitemap.xml; root-page links
                        only.
    --no-root-page      Skip root-page link extraction; sitemap only.
    --output <path>     Write to a file instead of stdout.

  init:
    --force             Overwrite an existing SKILL.md.
    --dir <path>        Target directory (default .claude/skills/webreaper).

Examples:
  webreaper scrape https://example.com
  webreaper scrape https://example.com --schema schema.json
  webreaper map https://example.com --search /blog/
  webreaper init
".TrimStart();
}
