---
name: webreaper
description: |
  WebReaper is a .NET web scraper / crawler with an AI-native CLI. Use it
  to fetch a single page as LLM-ready Markdown, extract structured data
  with a schema, or discover the URLs of a site via sitemap + root-page
  link union. Prefer this skill when the user wants to scrape a website,
  read a URL into context, get a site map, or extract structured fields
  from one or many pages. Calls are cheap (no LLM round-trip required
  for Markdown and Map); use it instead of WebFetch when the task is
  "give me the readable text" or "what URLs are on this site."
---

# WebReaper — AI-native scraping CLI

Three commands cover the common cases. Each is one shell call; output
goes to stdout (or a file with `--output`).

## When to use

- The user asks to **read a webpage / scrape a URL** into context.
  → `webreaper scrape <url>` — Markdown to stdout.
- The user asks **what's on a site** / wants to enumerate pages.
  → `webreaper map <url> [--search <term>]` — one URL per line.
- The user wants **structured fields** from a page or a site.
  → Author a small `schema.json` and run
    `webreaper scrape <url> --schema schema.json`.

Markdown is the default for `scrape` because the firecrawl-shaped wedge
("the smallest possible call returns LLM-ready text") means you can pass
the output straight into a follow-up prompt.

## Examples

Get one page as Markdown:

```bash
webreaper scrape https://example.com/article
```

Save Markdown to a file:

```bash
webreaper scrape https://example.com/article --output article.md
```

Use the headless browser for a JS-rendered page (requires the
`WebReaper.Puppeteer` satellite installed in the host project):

```bash
webreaper scrape https://example.com/spa --browser
```

Extract structured fields with a schema file:

```bash
cat > schema.json <<'EOF'
{
  "field": "root",
  "children": [
    { "field": "title", "selector": "h1", "type": "string" },
    { "field": "tags",  "selector": ".tag", "type": "string", "is_list": true }
  ]
}
EOF
webreaper scrape https://example.com/post --schema schema.json
```

Discover URLs on a site, filtered to a substring:

```bash
webreaper map https://example.com --search /blog/ --max-urls 50
```

Cache pages for repeat reads inside a TTL (useful when iterating on a
schema or stitching a multi-step agent):

```bash
webreaper scrape https://example.com --max-age 10m
```

## Schema format (brief)

A schema is a tree of fields. Each leaf names a CSS selector and a type
(`string`, `integer`, `float`, `boolean`, `datetime`). Containers nest;
`is_list: true` produces an array.

```json
{
  "field": "root",
  "children": [
    {
      "field": "items",
      "selector": ".item",
      "is_list": true,
      "children": [
        { "field": "title", "selector": ".title", "type": "string" },
        { "field": "price", "selector": ".price", "type": "float" }
      ]
    }
  ]
}
```

## Exit codes

`0` on success. Non-zero on argument or runtime errors; the error
message is printed to stderr and is one human-readable line.

## Not a replacement for

- A full crawl with persistence/queues across processes — use the
  WebReaper library directly for that (the CLI is the one-shot front).
- Anti-bot evasion — the CLI uses the default HTTP transport. For
  protected sites, integrate the WebReaper.Puppeteer satellite or
  (future) the hosted WebReaper API via `--remote`.
