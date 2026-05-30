# WebReaper Cloud Playground, Phase 1 Build Slice (Tier A, live HTTP to Markdown)

**Status:** Proposed build slice. Implements Phase 1 of [CLOUD-SCRAPE-PLAYGROUND-PLAN.md](CLOUD-SCRAPE-PLAYGROUND-PLAN.md).
**Date:** 2026-05-30
**Owner:** Alex
**Decision trail:** the 2026-05-30 plan + grill. Phase 0 (climb viz + homepage hero) shipped in PR #205. This slice stands up the live backend for the cheap, ungated tier and wires the whole pipeline end to end, so Phase 2 only adds the browser/stealth rungs.

## Goal

Ship Tier A: paste any URL, get clean Markdown, live, ungated, behind Turnstile + a rate limit. The point of doing the cheap tier first is to build and prove the **entire pipeline** (the existing climb component ↔ a Vercel edge gate ↔ a Fly backend ↔ an SSE stream) on the safe path. Tier A is a plain HTTP fetch, so there is no dramatic climb yet (that is Tier B); the win here is the wiring and the safety, not the wow.

## Architecture

```
[climb component, /playground]  --EventSource-->  [Vercel route handler /api/playground/scrape]
                                                     verifies Turnstile, per-IP rate limit, budget counter
                                                     proxies the SSE through (backend URL stays private)
                                                          |
                                                          v
                                  [Fly app: ASP.NET minimal API, references WebReaper]
                                    GET /scrape/stream?url=...  -> SSE of ClimbEvent
                                    Crawl(url).AsMarkdown()  (HTTP-only; no browser tiers => no climb)
                                    SSRF-guarded HttpMessageHandler; concurrency cap; per-job timeout
```

The SSE payload is the **existing `ClimbEvent` shape** (shipped in Phase 0). The component swaps its stubbed `playScript` source for an `EventSource`; the reducer is unchanged. That source-agnostic design was the whole point of Phase 0.

## Decisions

### 1. Backend: an ASP.NET Core minimal API referencing the WebReaper library

`GET /scrape/stream?url=<encoded>` returns `text/event-stream`. It builds a `ScraperEngineBuilder.Crawl(url).AsMarkdown()` engine with a custom streaming `IScraperSink` (seam confirmed: `WebReaper/Sinks/Abstract/IScraperSink.cs`), emits `request` / `attempt(http)` / `success` / `result(markdown)` `ClimbEvent`s, and reads the `RunReport` (`RunAsync` returns `Task<RunReport>`, confirmed) for the residual-block tally. GET + SSE because `EventSource` is GET-only; that keeps the client trivial.

### 2. SSRF for Tier A is app-level, and needs one small core seam (verified wrinkle)

Tier A is a plain server-side HTTP fetch of an attacker-supplied URL, so it needs SSRF defense, but **not** a per-job microVM (no hostile code executes; that cost is Tier B's). The proportionate control is a **guarded `HttpMessageHandler`**: resolve DNS, reject private / loopback / link-local (incl. `169.254.169.254`) and IPv6 equivalents, connect to the pinned IP (kills rebinding), http/https only, re-validate every redirect hop, cap response size + total time.

**The wrinkle (from the seam probe):** `HttpPageLoadTransport` only accepts a custom `HttpMessageHandler` through a *test-only* constructor (ADR-0083's response-stub seam); the public ctor is `(ICookiesStorage, IProxyProvider?, ILogger)`. So Tier A needs one of:
- **(a, recommended)** promote the handler-factory ctor to a public, documented seam, a generally useful capability (proxies, custom TLS, and now SSRF guards), reusable by any consumer;
- (b) a custom `IPageLoadTransport` wrapping a guarded `HttpClient`;
- (c) network-layer egress only (defers the whole control to Fly), heavier than warranted for an HTTP fetch.

Recommend (a) + the guarded handler. It is a small, additive core change and the right long-term seam.

### 3. Hosting: one small Fly app for Tier A, not per-job microVMs

An HTTP fetch is light; a single autoscaled, concurrency-capped Fly app with the guarded handler is enough. Per-job microVM isolation is a Phase 2 concern (Tier B's browser executing hostile content). Per-job timeout (~20s for HTTP), response-size cap, global concurrency cap.

### 4. Edge gate: a Vercel route handler that proxies the SSE

`/api/playground/scrape` verifies a Cloudflare Turnstile token, enforces a per-IP rate limit + a global daily counter (store: Upstash Redis or Vercel KV), then streams the backend SSE through (the Fly URL stays private, gating is centralized). Tier A has no LLM cost, so the $5/day ceiling barely applies here; the real Tier A guards are the rate limit + concurrency cap. The kill switch and LLM budget land with Tier B.

### 5. SSE contract: reuse `ClimbEvent`

Backend writes `data: <json ClimbEvent>\n\n` per event. Add an `EventSource` source in the component that feeds the same reducer; gate it behind a flag so `/playground` can point at local, staging, or canned.

## Repo placement

A new top-level `cloud/WebReaper.PlaygroundApi/` (sibling to `website/`). It is the seed of the WebReaper Cloud service, a deployable, not a library `Example/` or `Misc/` consumer, so it earns its own home like `website/` did.

## Buildable now (no accounts) vs needs your accounts

- **Now, locally verifiable:** the SSRF-guarded handler + the core injection seam + unit tests; the ASP.NET scrape service + streaming sink + SSE (curl-verifiable); the component's `EventSource` source wired to the local service.
- **Needs your accounts / secrets:** Fly.io (flyctl + account) to deploy; Cloudflare Turnstile site + secret keys; a rate-limit store (Upstash Redis / Vercel KV); Vercel env vars (backend URL, Turnstile secret).

## Build order (tracer-bullet, highest-risk-first)

1. **SSRF-guarded `HttpMessageHandler` + the public injection seam + unit tests.** The security spine; build and test before anything is reachable. Pin the blocklist (private/loopback/link-local/IPv6), DNS-pinning, redirect re-validation, scheme allowlist, size/time caps.
2. **The ASP.NET scrape service:** `GET /scrape/stream?url=` SSE of `ClimbEvent`s via `Crawl(url).AsMarkdown()` + the guarded handler + streaming sink. Run and curl locally.
3. **Component `EventSource` source:** feed the same reducer; point a flagged `/playground` surface at the local service; verify end to end locally.
4. **Vercel edge gate:** Turnstile verify + per-IP rate limit + budget counter + SSE proxy. (Needs Turnstile keys + a store.)
5. **Fly deploy:** Dockerfile + fly.toml, concurrency cap, timeouts, health check. (Needs Fly account.)

## Open prerequisites for the owner

- **Fly.io account** (or a different container host) for steps 4 to 5.
- **Cloudflare Turnstile** keys (you already have Cloudflare via the `webreaper.ai` domain).
- **Rate-limit store:** Upstash Redis vs Vercel KV.

## Non-goals (this slice)

- Browser / stealth tiers and the live climb (Phase 2, Tier B).
- The signup/email gate, LLM extraction, the $-budget kill switch (Tier B).
- Per-job microVM isolation (Tier B).
- Capturing a real climb recording for the hero (separate, needs Tier B).
