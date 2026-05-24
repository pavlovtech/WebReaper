# `WebReaper.Cdp` — raw Chrome DevTools Protocol transport satellite, the bedrock for **Browser backend** swaps

## Status

**Accepted — implementation complete** (2026-05-24). Slice 1 of 4 in the **Transports wave** (v10.0.0
target): ADR-0052 (this one, CDP-direct), ADR-0053 (Playwright satellite +
Puppeteer deletion), ADR-0054 (`WebReaper.Stealth.CloakBrowser`), ADR-0055
(CLI browser/stealth acquisition policy). New satellite per ADR-0009 — core
stays dependency-light and AOT-clean.

## Context

ADR-0004 collapsed page loading into one seam: `IPageLoader` dispatches on
`PageRequest.PageType` to one of two `IPageLoadTransport` adapters — the
HTTP transport (Static) and the browser transport (Dynamic). Through
v10.0.0 the Dynamic slot has had exactly one occupant: `BrowserPageLoadTransport`
in `WebReaper.Puppeteer` (ADR-0009). The seam was already "two adapters,
one mechanism each"; the *naming* of the second adapter as "Browser"
was honest because there was one browser SDK driving the slot.

The Transports wave breaks that one-SDK assumption from three angles
simultaneously:

1. **Stealth Chromium forks** (CloakBrowser, Patchright, Camoufox) ship as
   raw Chromium binaries you launch with `--remote-debugging-port=N` and
   talk to over CDP. They do not ship PuppeteerSharp or Microsoft.Playwright
   bindings; the only seam they expose is CDP itself. A `WebReaper.Stealth.X`
   satellite cannot use the Puppeteer transport — it has to speak CDP
   directly to the spawned binary.
2. **The CLI's AOT guarantee** (ADR-0043) ships a single Native-AOT binary
   across 6 RIDs. PuppeteerSharp and Microsoft.Playwright both depend on
   reflection-heavy serialisation that is not AOT-clean inside a single
   binary (the satellites stay library-usable in JIT consumers; baking
   them into the CLI binary is not). The CLI needs a *transport* it can
   bake without forfeiting AOT — a raw-CDP client built on
   `System.Net.WebSockets` + source-generated JSON.
3. **BYO scenarios** — testing, sidecar architectures, users who already
   have a Chrome running with `--remote-debugging-port=9222`, a remote
   browser farm — all want "connect to this CDP URL" and nothing else.
   The Puppeteer transport's "launch a managed Chromium" shape doesn't
   fit; a thin connect-only transport does.

CONTEXT.md's revised **Load transport** glossary now lists three transports
(HTTP, Playwright, CDP-direct) and a new sibling axis: **Browser backend**.
A backend is the Chromium binary a browser transport drives. The CDP-direct
transport is the *one transport* that drives many backends — that's its
job. Playwright drives only what Microsoft.Playwright ships (Chromium,
Firefox, WebKit, all vanilla); the CDP-direct transport drives anything
that exposes a CDP endpoint, including every stealth fork.

## Decision

- **New satellite `WebReaper.Cdp`** (NuGet package, satellite-pattern per
  ADR-0009). Core never references it; the dependency enters the
  consumer's graph only when they `dotnet add package WebReaper.Cdp` and
  `using WebReaper.Cdp;`.

- **`CdpPageLoadTransport : IPageLoadTransport`** lives in the satellite.
  Implements the existing ADR-0004 seam. Wired through the standard
  ADR-0050 4-arg `WithLoadTransport(Func<ICookiesStorage, IProxyProvider?, ILogger, IActionResolver, IPageLoadTransport>)`
  factory contract. The transport accepts the 4-tuple even where it
  ignores arguments (e.g. cookies handling differs in CDP from
  PuppeteerSharp's `CookieContainer` bridge — see Accepted cost).

- **Two public builder overloads** in `WebReaper.Cdp.CdpPageLoaderBuilderExtensions`:

  ```csharp
  // Connect-to-existing: BYO browser, transport just opens the WebSocket
  ScraperEngineBuilder WithCdpPageLoader(this ScraperEngineBuilder b, string cdpUrl);

  // Launch-and-connect: transport spawns Chromium with --remote-debugging-port=0,
  // waits for the port, connects, manages lifecycle, tears down on Dispose
  ScraperEngineBuilder WithCdpPageLoader(this ScraperEngineBuilder b, CdpLaunchOptions opts);
  ```

  `CdpLaunchOptions` carries: executable path (default = PATH-detected
  Chrome / Chromium / Edge in that order), additional command-line flags
  (`--headless=new`, `--no-sandbox`, …), startup timeout, user-data-dir
  policy. The launch path internally calls `CdpLaunchHelpers`.

- **Public `CdpLaunchHelpers` static utility class** in the satellite — the
  shared layer the `WebReaper.Stealth.X` satellites depend on:

  ```csharp
  public static class CdpLaunchHelpers {
      // Find an executable by name across PATH and platform-conventional install dirs
      public static string? FindOnPath(params string[] candidateNames);

      // Spawn `executable` with the given args + `--remote-debugging-port=0`,
      // wait for the port to be readable, return the resolved WebSocket CDP URL
      public static Task<LaunchedCdpEndpoint> LaunchAsync(CdpLaunchSpec spec, CancellationToken ct);

      // Validate that the URL responds to a CDP "Browser.getVersion" probe within timeout
      public static Task<bool> ProbeAsync(string cdpUrl, TimeSpan timeout, CancellationToken ct);
  }
  ```

  Stealth satellites compose `CdpLaunchHelpers.LaunchAsync(...)` →
  resulting CDP URL → `WithCdpPageLoader(url)` (connect-to-existing
  overload). The transport itself stays unaware of which fork launched
  the browser.

- **PageAction dispatch — six concrete arms via raw CDP primitives.** The
  ADR-0035 closed sum has seven arms today; the seventh is `SemanticAct`
  (ADR-0050, resolved by `IActionResolver`). The transport dispatches the
  six concrete arms through CDP `Input.*` / `Runtime.evaluate` /
  `Page.navigate` directly. Behaviour parity with the Puppeteer
  transport's table is the acceptance criterion — pinned by the existing
  `SemanticActDispatchTests` plus a new `CdpPageActionDispatchTests`
  derived from the same shape.

- **`SemanticAct` (ADR-0050) preserved end to end.** The transport
  receives the resolver argument from the 4-arg factory; on a
  `SemanticAct(intent)` arm it delegates to `IActionResolver.ResolveAsync`,
  receives one of the six concrete arms back, dispatches it via the
  primitive layer above, and caches the resolution per crawl per intent
  string (`SemanticActCoordinator` already lives in core — the transport
  is a delegator, not a re-implementer).

- **AOT-clean by construction.** No reflection-driven serialisation. CDP
  messages are JSON; the transport uses `System.Text.Json` source-gen
  (`WebReaperCdpJson` context, sibling to the existing
  `WebReaperAgentJson` from ADR-0051). The transitive dep is
  `System.Net.WebSockets` (in-box) and nothing else. The
  `WebReaper.AotSmokeTest` is extended to publish a consumer that pulls
  the satellite and asserts zero IL/AOT warnings — same guardrail the
  rest of the satellites have.

- **Cookies and proxies — same shape as ADR-0009 documented.** Cookies:
  `ICookiesStorage`'s `CookieContainer` is projected to CDP
  `Network.setCookies` calls before the first navigation; on navigation
  completion CDP `Network.getAllCookies` is reverse-projected back into
  the container. Proxy: `IProxyProvider`, when set, supplies a proxy URL
  that becomes the launched Chromium's `--proxy-server=` flag (the same
  *application* the Puppeteer transport used). The connect-to-existing
  overload assumes the BYO browser already has its proxy configured;
  passing `IProxyProvider` with that overload logs a warning and is
  ignored.

## Considered options

- **CDP-direct as a new satellite (chosen).** Matches ADR-0009 per-technology
  packaging. Stealth satellites depend on `WebReaper.Cdp`; users who only
  want BYO-CDP add one package; the dependency stays out of core.
- **Fold CDP-direct into core (rejected).** `System.Net.WebSockets` is
  in-box and the source-gen JSON is light, so the *weight* argument doesn't
  hold here. But the *concentration* argument from ADR-0009 still does: a
  consumer doing a plain HTTP→JSON-lines crawl gains an unused CDP client
  and a `PageActions` dispatch layer. More importantly, the `WebReaper.Cdp`
  package becomes the shared dependency of all `WebReaper.Stealth.X` satellites;
  having it as a separate addressable package is the right packaging
  primitive. Inverting that into core would force every consumer to pay for
  the CDP layer to enable stealth scenarios that most consumers don't run.
- **Skip CDP-direct, force stealth users through Playwright (rejected).**
  Stealth Chromium forks don't ship Playwright bindings. A user wiring
  CloakBrowser via Playwright would have to call
  `playwright.chromium.connect_over_cdp(...)` — re-introducing CDP, just
  inside Playwright. Strictly worse than CDP-direct: extra dependency
  weight, indirection through Microsoft.Playwright's connection logic,
  and the AOT-CLI story collapses (Microsoft.Playwright is not AOT-clean).
- **Build CDP-direct on top of PuppeteerSharp's CDP client (rejected).**
  Defeats every motivation. The wave deletes the Puppeteer satellite in
  ADR-0053; building the bedrock on a dependency we're removing is
  incoherent. PuppeteerSharp's CDP client is also not AOT-clean.
- **Two transport satellites that share an internal `IBrowserTransport`
  super-seam (rejected).** Would unify Playwright and CDP-direct under one
  abstraction. The Dynamic-slot singleton constraint stays — `IPageLoader`
  holds at most one browser transport at a time — so the super-seam earns
  no rent in v10. Premature; revisit if a third browser transport
  (WebDriver-BiDi, e.g.) appears.

## Accepted cost

- **Cookie projection runs per-navigation, not per-request.** PuppeteerSharp
  bridges `CookieContainer` once at launch. CDP's `Network.setCookies` is
  per-target; the transport re-applies the container on each navigation to
  keep parity with the HTTP transport's "cookies are crawl-wide state"
  contract. Measured cost is a small CDP round-trip per page; negligible
  versus the page-load cost itself.
- **`PageActions` dispatch is the satellite's responsibility — duplicated
  with ADR-0053's Playwright transport.** Six arms × two transports = two
  dispatch tables, both shadowing the deleted Puppeteer table. This is the
  ADR-0004 cost we already accepted ("per-mechanism client/launch quirks
  live in the transport"). A future ADR may extract a shared
  `PageActionDispatcher` over CDP primitives once the pattern has stabilised
  across two adapters; v10 ships the duplication knowingly.
- **`PageRequest` carries `Headless` and `PageActions` that the HTTP
  transport ignores** — the ADR-0004 "mildly fat request type" cost is
  preserved. The CDP transport reads both fields; no widening needed.

## Deliberate consequences

- **The Dynamic-slot picker becomes meaningful.** Before this wave, users
  wired the Browser transport by adding `WebReaper.Puppeteer` and calling
  `.WithPuppeteerPageLoader()` — only one choice. From v10 onward, the user
  picks between `WebReaper.Playwright` (`.WithPlaywrightPageLoader(...)`)
  and `WebReaper.Cdp` (`.WithCdpPageLoader(...)`) plus the stealth
  satellites that compose on top of `WebReaper.Cdp`. The
  `BrowserNotConfiguredPageLoadTransport`'s actionable error message
  (the one in core that throws when Dynamic load is requested without a
  transport registered) is extended in lockstep — see ADR-0053.
- **Stealth satellites become thin.** Each `WebReaper.Stealth.X` is
  conceptually: per-fork binary discovery + per-fork launch flag set +
  `WithCdpPageLoader(url)` wiring. The shared work lives in
  `CdpLaunchHelpers`; per-satellite work is the per-fork details. See
  ADR-0054 for the documented recipe.
- **CDP-direct is the CLI's only baked browser transport.** Per ADR-0055,
  `WebReaper.Cli` references `WebReaper.Cdp` but never `WebReaper.Playwright`
  or any `WebReaper.Stealth.X` — the CLI's auto-spawn path and `--browser-cdp-url`
  flag both produce a CDP URL the baked transport connects to. The AOT
  guarantee survives.

## SemVer

**Minor (additive).** The transport is new public surface in a new
satellite package; nothing in core or existing satellites changes
behaviour because of this ADR. The deletion of `WebReaper.Puppeteer`
(carried by ADR-0053) is the **major** break of v10.0.0; the satellites
ship together in the lockstep wave.
