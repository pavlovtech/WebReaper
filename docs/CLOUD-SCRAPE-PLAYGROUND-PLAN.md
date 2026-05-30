# WebReaper Cloud Scrape Playground, Design + Build Plan

**Status:** Proposed. A live "try the real thing" playground on the marketing site that doubles as the first slice of WebReaper Cloud.
**Date:** 2026-05-30
**Owner:** Alex
**Decision trail:** the 2026-05-30 playground design grill. Two scope picks were made up front (any-URL, full pipeline) and are deliberately *refined* below by the prior-art finding. Sits under the open-core + managed-cloud strategy (this playground is WebReaper Cloud's first endpoint).

## One-line goal

Let a visitor paste a URL on the site and watch the real WebReaper pipeline scrape it live, the HTTP to browser to stealth escalation climb visible as it happens, then see clean Markdown / JSON out. The backend that serves this *is* the WebReaper Cloud scrape endpoint, so the marketing demo and the paid product are one system.

## What this commits you to (read this first)

The maximal pick (ungated + any URL + full browser/stealth/LLM) is not a demo, it is a public, unauthenticated, arbitrary-URL fetcher that drives a real browser and spends LLM tokens per request. That is two things at once:

1. **An SSRF-and-cost magnet.** Anyone can aim it at internal IPs or cloud metadata, and every run costs real money (Chromium CPU/RAM + LLM tokens).
2. **The seed of WebReaper Cloud.** The hardening you do here (isolation, egress control, rate limits, budget caps, gating) is exactly the managed product's free tier. Build it as a service, not a throwaway.

So: design-first, and do not ship the expensive path ungated.

## Prior art (what the market already does)

A live "paste a URL, see it scrape" playground is table stakes in this category. Not having one is a visible gap against the product the site positions against.

- **Firecrawl, [the bar to beat](https://www.firecrawl.dev/playground).** Paste any URL, runs the *same pipeline as the paid API*, returns Markdown / JSON / screenshot / metadata, scrape/crawl/map/extract modes, ungated (autoruns an example with no account). This is the direct competitor and exactly the ask.
- **Jina Reader, [the ungated-any-URL precedent](https://jina.ai/reader/).** Prefix `r.jina.ai/<url>` to get clean Markdown from any URL, free, no signup. Notably it is the *lightweight HTTP+Markdown* shape, and it controls cost purely with rate limits (20 req/min with no key, 500 with a free key). Proves the ungated model works, on the cheap path.
- **The full-browser crowd all gate it.** [Browserless](https://www.browserless.io/blog/rest-api-playground), [Zyte](https://www.zyte.com/blog/play-before-you-scrape-explore-zyte-api-settings-with-playground/), [Oxylabs](https://developers.oxylabs.io/scraping-solutions/web-scraper-api/web-scraper-api-playground), [Bright Data](https://docs.brightdata.com/scraping-automation/scraping-browser/features/playground), [Scrapeless](https://www.scrapeless.com/en/blog/scrapeless-scraping-browser-playground). All run real Chrome, all sit **behind a dashboard / account**.

**What the market teaches:** the ungated, instant ones lean on the cheap path + rate limits (Jina); the full-browser ones gate behind signup precisely because of the cost / abuse surface. Nobody gives away unauthenticated full-browser + stealth + LLM on arbitrary URLs with no gate. That is not a gap in the market, it is the market avoiding the expensive corner you picked.

**Where you would actually be novel:** no surveyed playground *visualizes the escalation climb* (HTTP -> 403 -> browser -> still blocked -> stealth -> success). That animation is your bot-bypass story made visible, and it is the one thing here that is not "Firecrawl but ours." Treat it as the headline, not the any-URL box.

## Pinned recommendation

**Two tiers, both accept any URL, only one is ungated.**

- **Tier A, the ungated taste (front-page wow).** HTTP fetch to clean Markdown only. No headless browser, no LLM, no account. A few runs/day/IP behind a Turnstile challenge. This is Jina-parity: cheap, instant, safe, and "any page to clean Markdown" is your clearest value story.
- **Tier B, the full pipeline (gated).** Browser + stealth escalation + LLM extraction, behind a lightweight email/signup gate, hard per-job caps, and a daily spend kill switch. This is Firecrawl / dashboard-crowd parity and the WebReaper Cloud free tier seed.

This honors the any-URL ambition (both tiers take any URL) while refusing to hand the expensive path to anonymous traffic, which the entire full-browser market confirms is the right call.

**Supporting decisions:**

- **Wallet:** you eat a *capped* cost on Tier B as marketing spend. Cheap model (Haiku) for demo extraction, hard token cap per run, daily dollar ceiling with a kill switch ("demo at capacity, sign up"). Offer BYO-key to lift the per-user limit (zero cost to you, converts power users).
- **Hosting:** Vercel stays UI + edge gate (Turnstile, rate limit, budget gate) only. The scrape backend runs on **Fly Machines** (per-request microVMs, fast boot, scale to zero, egress controllable), with Cloud Run jobs as the alternative. Vercel's serverless model cannot host the browser/stealth path (time caps, ephemeral FS, no persistent subprocess, ~220MB stealth download).
- **The escalation-climb visualization is the headline UX**, not an afterthought.

**Considered and rejected:** the raw pick, ungated full pipeline on any URL. Rejected because it is an open SSRF pivot and an unbounded LLM/compute bill exposed to anonymous traffic, and because no competitor does it, for those reasons. Tier B keeps the capability, behind a gate.

## The hard parts (non-negotiable for any public any-URL fetcher)

### 1. SSRF + hostile content (applies to BOTH tiers, Tier A still fetches arbitrary URLs)

App-level URL checks are not enough once a browser is driving. Enforce egress at the **network layer**:

- Per-job network namespace + egress proxy that blocks RFC1918, loopback, link-local (incl. `169.254.169.254` cloud metadata), and IPv6 equivalents.
- **DNS pinning:** resolve the hostname, validate the resolved IP against the blocklist, then connect to *that pinned IP*, to kill DNS-rebinding TOCTOU.
- Re-validate every redirect hop. Scheme allowlist: http/https only (no `file://`, `gopher://`, etc.).
- Each job runs in an **ephemeral sandbox** (one microVM/container per request, killed on timeout). You are executing attacker-supplied web content server-side; one job must not see another's data or escape.

### 2. Cost ceiling (the one that can actually hurt)

Hard per-job timeout (~45s), output size cap, per-IP daily cap, global concurrency cap, and a **daily dollar kill switch**. Tier B only: Haiku, token cap per run. When the daily budget is hit, flip the playground to a signup CTA.

### 3. Gating + abuse + legal

Turnstile on Tier A; email/signup on Tier B. Short result retention (TTL, not permanent). Abuse logging + rate limits. A ToS line covering "do not scrape what you are not allowed to," since a public scraper is a proxy for someone else's requests.

## Architecture

```
[Vercel: playground UI]
   -> POST /scrape {url, tier}
   -> [edge: Turnstile + rate-limit + daily-budget gate]
   -> [scrape backend on Fly Machines: ephemeral isolated worker per job]
        Tier A: webreaper scrape (HTTP) -> HtmlToMarkdown
        Tier B: webreaper scrape (HTTP -> browser -> stealth climb) + LLM extract
        egress locked at network layer; DNS-pinned; killed on timeout
        emits NDJSON progress events on stderr
   -> stream events back (SSE) -> UI renders the live climb + Markdown/JSON
```

## Reused vs new

- **Reused (WebReaper):** the CLI `scrape`, the ADR-0083 HTTP->browser->stealth escalating loader (the climb *is* the demo), `HtmlToMarkdown`, the LLM extractor, RunReport telemetry.
- **Small CLI affordance needed:** structured progress events, an NDJSON stream on stderr (one event per escalation step + status), behind a `--progress json` flag, so the backend can stream the climb live. Today the CLI emits results, not step-by-step progress; verify and add this.
- **New (the service):** the Fly Machines job runner with per-job sandbox + locked egress; the SSRF egress proxy + DNS pinning; the Vercel edge gate (Turnstile + rate limit + budget); the playground UI with the SSE-driven escalation animation; the daily-budget kill switch.

## Build order (highest-risk-first, ship the safe real thing early)

0. **Prototype the UI against a stubbed NDJSON stream.** Zero infra, zero cost, zero security surface. Validate the escalation-climb UX before any backend exists. (This is the cheap parallel win, start it now.)
1. **Tier A live.** Ungated HTTP->Markdown, any URL, on a small Fly container, behind Turnstile + per-IP rate limit + the SSRF egress guard. Ships the safe, real thing fast and matches Jina.
2. **Tier B live.** Full pipeline behind the signup gate + caps + daily kill switch, per-job microVM sandbox on Fly Machines. The Cloud free-tier seed.
3. **Graduate to WebReaper Cloud.** Accounts, API keys, billing, the managed product from the monetization plan. Tier B's design must not foreclose this.

## Open calls for the owner

- **Daily dollar ceiling for Tier B** (drives every cap). A number you are comfortable burning as marketing.
- **Gate type for Tier B:** email capture, full signup, or OAuth.
- **Wallet default for Tier B:** you eat capped cost, vs BYO-key required. (Recommendation: you eat it, BYO-key optional to lift limits.)

## Non-goals (v0)

- Accounts, API keys, billing (Phase 3).
- Crawl / map in the public playground (unbounded cost; scrape-one-URL only first).
- Custom browser actions / BYO-browser in the public playground.
- Long-term persistence of user scrape results (short TTL only).
