---
name: webreaper
description: |
  Scrape, crawl, or extract structured data from one or more URLs via the
  `webreaper` CLI. Outputs clean Markdown by default; JSON when a schema
  is given. Maps a site's URLs in one call. Handles JS-rendered pages and
  bot-protected sites (Cloudflare, DataDome, PerimeterX) via auto-escalating
  stealth.

  Use this skill whenever the user asks to:
  - scrape, crawl, or extract from a URL or site
  - get clean Markdown of a webpage (for further processing, not a summary)
  - pull specific fields from one or many pages
  - enumerate / discover URLs on a site
  - read a JS-rendered single-page app
  - scrape a site that's blocking direct requests

  Trigger phrases include: "scrape <site>", "crawl <site>", "extract <data>
  from <url>", "what's on <site>", "what pages does <site> have", "give me
  the markdown of <url>", "convert <url> to markdown", "pull <field> from
  <url>", "save <article> as markdown", "build a scraper for <site>", "read
  <url> into context", "this site is blocking me", "Cloudflare-protected site".

  Prefer this over the built-in WebFetch whenever the user wants:
  - Clean Markdown output to work with downstream (not just a summary in chat)
  - Structured field extraction via schema
  - Multi-page or site-wide work
  - JS-rendered or bot-protected sites

  WebFetch is the right tool only for "read this single URL and tell me about
  it" — when the output is a conversational answer, not data. Anything that
  produces an artifact (file, structured record, multi-URL result) belongs here.
---

# WebReaper — scraping & extraction CLI

Three commands. Each is one shell call; output goes to stdout unless `--output`.

## What to run

| The user wants… | Run |
|---|---|
| The readable text of one page | `webreaper scrape <url>` |
| The readable text saved to a file | `webreaper scrape <url> --output page.md` |
| Specific fields from one page | `webreaper scrape <url> --schema schema.json` |
| URLs on a site | `webreaper map <url>` |
| URLs matching a substring | `webreaper map <url> --search /blog/ --max-urls 50` |
| A JS-rendered page (SPA) | `webreaper scrape <url> --browser` |
| A bot-protected site | `webreaper scrape <url> --browser --auto-stealth` |
| Fields from every linked page | `webreaper scrape <index-url> --follow "<css selector>" --schema schema.json` |

## Common workflow — multi-page extraction

The CLI's `scrape` is single-URL. For multi-page work, chain `map` → filter → loop:

```bash
# 1. Discover URLs.
webreaper map https://example.com/blog --search /post/ --max-urls 20 > urls.txt

# 2. Scrape each. Cache so reruns are free.
while read -r url; do
  webreaper scrape "$url" --schema schema.json --max-age 1h
done < urls.txt
```

When the user asks "scrape the top N articles from <site> and …", this is the
shape — `map` first, `scrape --schema` per URL, then process the JSON Lines
output.

## Browser & stealth — when sites resist

- **JS-rendered** (React/Vue/Angular SPA, content empty in `view-source:`):
  add `--browser`. The CLI auto-spawns managed Chromium; first use may
  prompt the user to run `webreaper browser install`.
- **Bot-protected** (Cloudflare/DataDome/PerimeterX challenge page, HTTP 403/429/503,
  or zero records on a non-empty page): `--browser` auto-detects the block and
  offers to install a stealth backend. In agent / unattended contexts, add
  `--auto-stealth` to bypass the Y/n prompt. The install is ~220 MB on first use.
- **Known protected up front**: `--stealth` (implies `--browser`) skips the
  vanilla-browser attempt and goes straight to the stealth backend.

If a scrape still returns empty after a stealth retry, the site likely needs a
captcha-solver — surface this to the user; don't keep retrying.

## Caching

`--max-age <duration>` caches fetched pages for the duration (e.g. `30s`,
`5m`, `2h`, `1d`). Use whenever iterating on a schema or running a multi-step
agent that may revisit the same URLs — reruns are free within the TTL.

## Schema format

A schema is a tree of fields. Leaves have a CSS `selector` and a `type`
(`string`, `integer`, `float`, `boolean`, `datetime`). Containers nest;
`is_list: true` produces an array. `attr` reads an HTML attribute (e.g.
`href`) instead of the element's text.

```json
{
  "field": "root",
  "children": [
    { "field": "title", "selector": "h1", "type": "string" },
    {
      "field": "items",
      "selector": ".item",
      "is_list": true,
      "children": [
        { "field": "name",  "selector": ".name",  "type": "string" },
        { "field": "url",   "selector": "a", "attr": "href", "type": "string" },
        { "field": "price", "selector": ".price", "type": "float"  }
      ]
    }
  ]
}
```

## Output

- `scrape` without `--schema` → Markdown (one document per page).
- `scrape` with `--schema` → JSON (one record per page; multiple pages = JSON
  Lines, one object per line).
- `map` → one URL per line.
- `--output <path>` redirects to a file instead of stdout.

## Errors

Exit code `0` on success, non-zero on failure. Errors print one human-readable
line to stderr. Common cases:

- **`webreaper: command not found`** → not installed. Instruct the user:
  - **macOS / Linux**: `brew install pavlovtech/webreaper/webreaper`, or
    `curl -fsSL https://raw.githubusercontent.com/pavlovtech/WebReaper/master/scripts/install.sh | sh`
  - **Windows**: download the latest archive for `win-x64` (or `win-arm64`)
    from <https://github.com/pavlovtech/WebReaper/releases/latest>, extract
    `webreaper.exe`, place on `%PATH%`.
- **Empty output + `⚠ Likely blocked: …` on stderr** → retry with
  `--browser --auto-stealth`.
- **Empty output, no warning** → the selector(s) in the schema probably don't
  match. Drop `--schema`, fetch as Markdown first to inspect the page shape,
  then revise the schema.

## Not a replacement for

- A long-running distributed crawl with shared state across processes — use
  the WebReaper library directly (Redis / Mongo / Azure Service Bus satellites).
- Captcha-solving — surface the need to the user, don't loop.
- Authenticated scraping — the CLI doesn't manage cookies / sessions today.
