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
    --browser           Use the headless-browser transport (requires the
                        WebReaper.Puppeteer satellite).
    --follow <selector> Follow links matching this CSS selector before
                        extraction (the Crawl chain's first step).

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
