# `WebReaper.Playwright` — Playwright transport satellite; clean-cut deletion of `WebReaper.Puppeteer` in the same release

## Status

**Proposed** (2026-05-24). Slice 2 of 4 in the **Transports wave** (v11.0.0
target). Carries the **major break** of v11.0.0: the `WebReaper.Puppeteer`
satellite and its `WebReaper.Puppeteer.Tests` companion are deleted in the
same release that ships `WebReaper.Playwright`. Actions the future named
in ADR-0009 ("Puppeteer becomes removable as a package-drop — a stated
2026 direction, recorded here, deliberately not actioned here").

## Context

ADR-0009 sized this break two years ago. The satellite quarantine was the
prerequisite ("**Puppeteer becomes removable as a package-drop**, not core
surgery"). Two years on, the conditions have crystallised:

1. **Microsoft.Playwright is the modern .NET-native browser SDK.** Official
   Microsoft maintenance; idiomatic `Task`-returning API; multi-browser
   (Chromium, Firefox, WebKit); better network interception (`page.RouteAsync`
   over PuppeteerSharp's older interception API); auto-wait semantics that
   reduce the need for explicit `WaitForSelector` in user code.
2. **Stealth Chromium forks have made PuppeteerSharp a strict liability.**
   CloakBrowser, Patchright, Camoufox, undetected-chromedriver all ship as
   bare Chromium binaries you talk to via raw CDP. PuppeteerSharp's
   `connectAsync` path works but adds the entire PuppeteerSharp dependency
   plus its CDP client to the consumer graph for no functional gain over
   the `WebReaper.Cdp` satellite (ADR-0052).
3. **The ADR-0050 4-arg widening was the last addition** to the Puppeteer
   transport. Its action-dispatch table covers exactly four `PageActionType`
   arms — `WaitForSelector` and `EvaluateExpression` throw at runtime — a
   limitation called out in ADR-0004 §"Out of scope" and never closed. The
   Playwright transport ships full seven-arm coverage on day one.

The wave is structurally aligned: ADR-0052 introduced the `WebReaper.Cdp`
bedrock that absorbs stealth use cases; this ADR retires the SDK-shaped
predecessor and replaces it with the supported one. ADR-0054 + ADR-0055
build on top.

## Decision

- **New satellite `WebReaper.Playwright`** (NuGet package, satellite pattern
  per ADR-0009). Depends on `Microsoft.Playwright` (the current GA major).
  Core never references it; consumers add the package and the using
  directive — same shape as every other satellite.

- **`PlaywrightPageLoadTransport : IPageLoadTransport`** lives in the
  satellite. Implements the ADR-0004 seam directly (not layered on
  `CdpPageLoadTransport`); Microsoft.Playwright drives Chromium, Firefox,
  and WebKit through three different protocols (CDP, Juggler-fork, Web
  Inspector) and reusing the CDP transport would lose Firefox/WebKit and
  double-wrap Chromium. Independent transports, same singleton-Dynamic-slot
  constraint as before.

- **Public builder extension** in `WebReaper.Playwright.PlaywrightPageLoaderBuilderExtensions`:

  ```csharp
  ScraperEngineBuilder WithPlaywrightPageLoader(
      this ScraperEngineBuilder b,
      PlaywrightBrowser browser = PlaywrightBrowser.Chromium,
      PlaywrightLaunchOptions? opts = null);

  public enum PlaywrightBrowser { Chromium, Firefox, Webkit }
  ```

  `PlaywrightLaunchOptions` (a thin wrapper around Microsoft.Playwright's
  `BrowserTypeLaunchOptions`) carries headless flag, channel (e.g.
  `"chrome"`, `"msedge"`), proxy, executable path override, additional
  args. Defaults match ADR-0004's `Networkidle2` navigation convention via
  Playwright's `WaitUntilState.NetworkIdle`.

- **All seven `PageAction` arms implemented.** Maps to Playwright's
  `IPage`/`IElementHandle` APIs:
  - `Click(sel)` → `page.ClickAsync(sel)`
  - `Type(sel, text)` → `page.FillAsync(sel, text)`
  - `WaitForSelector(sel, timeoutMs)` → `page.WaitForSelectorAsync(sel, new() { Timeout = timeoutMs })`
  - `EvaluateExpression(js)` → `page.EvaluateAsync(js)`
  - `WaitForNavigation` → `page.WaitForNavigationAsync()` (deprecated in
    Playwright; mapped to `page.WaitForLoadStateAsync` for parity)
  - `Scroll` → `page.EvaluateAsync(window.scrollBy(0, opts.dy))`
  - `SemanticAct(intent)` (ADR-0050) → delegates to `IActionResolver`,
    dispatches the resolved arm through this same table, caches per crawl
    per intent string via `SemanticActCoordinator`.

  Replacing the four-arm Puppeteer coverage that left two arms throwing
  at runtime closes the ADR-0004 §"Out of scope" gap.

- **Multi-browser default = Chromium.** `WithPlaywrightPageLoader()` with
  no argument launches Chromium; the `PlaywrightBrowser` parameter opts
  into Firefox or WebKit. Integration tests in v11 cover Chromium only —
  the existing flaky `WebReaper.IntegrationTests` suite is too slow
  (live `alexpavlov.dev`, `Task.Delay` up to 25s) to triple. Firefox/WebKit
  parity tests are explicitly community-contributable and documented as
  such in the satellite's `README`.

- **`WebReaper.Puppeteer` satellite is deleted in the same release.** Clean
  cut, no `[Obsolete]` deprecation window. Three .cs files in the
  satellite proper + the `WebReaper.Puppeteer.Tests` project + the
  `Examples/WebReaper.ConsoleApplication` and `Examples/BrownsfashionScraper`
  consumers + the `WebReaper.IntegrationTests/ScraperTests.cs` and
  `RedisDistributedAdapterTests.cs` references + the unit-tests'
  `HttpOnlyDefaultPageLoaderTests.cs` + `SemanticActDispatchTests.cs`
  all migrate to the Playwright transport in one wave PR.

- **The `BrowserNotConfiguredPageLoadTransport` error string** (core's
  default Dynamic-slot occupant that throws actionable migration text) is
  updated from *"add WebReaper.Puppeteer"* to:

  > Dynamic page loading requires a browser transport. Wire one of:
  > • `WithPlaywrightPageLoader(...)` — `WebReaper.Playwright` satellite
  > • `WithCdpPageLoader(...)` — `WebReaper.Cdp` satellite (for stealth backends)

  The message change is part of this ADR's core surgery (the one core file
  touched by the wave).

- **CLAUDE.md's transport listing flips in lockstep.** The "page loaders"
  row of the seam table changes from `Http + Puppeteer` to
  `Http + Playwright + Cdp`; the "First dynamic-page run downloads
  Chromium via Puppeteer" gotcha flips to "First Playwright run downloads
  browsers via `playwright install` (the satellite's standard first-run
  step)".

## Considered options

- **Clean cut: ship Playwright, delete Puppeteer same release (chosen).**
  Matches ADR-0009's exact precedent. The compat-shell argument doesn't
  apply here: a shell forwarding `WithPuppeteerPageLoader()` to
  `WithPlaywrightPageLoader()` would have wildly different runtime
  behaviour (different browser-launch quirks, different action-dispatch
  semantics, different network-interception API surface). A "compatibility
  forwarder" that silently changes the runtime is worse than a compile-time
  error pointing at the new method.
- **Parallel-ship: both satellites coexist; mark Puppeteer `[Obsolete]`
  with one-major deprecation window, remove in v12 (considered, rejected).**
  Was the original recommendation in HITL Round 1; user reversed it to
  clean-cut. Cost: extra ongoing maintenance of a satellite the project
  is moving off; users on the deprecation path stuck on a less-capable
  transport for an entire release cycle; ambiguous "which is the future"
  signal.
- **Skip Playwright entirely, just delete Puppeteer; leave CDP-direct as
  the only browser option (rejected).** CDP-direct is too low-level for
  casual users — wiring `page.click` via raw CDP is not the abstraction
  level most .NET consumers want for a one-off scrape. Playwright is the
  modern equivalent of the abstraction Puppeteer provided. Removing
  Puppeteer without replacing the abstraction layer would regress the
  library.
- **Keep Puppeteer indefinitely, ship Playwright as a second option
  (rejected).** Two browser-SDK satellites in permanent parallel ship
  maintenance debt for negative gain. ADR-0009's named direction has been
  on the table for two years; not actioning it indefinitely is the worst
  of both worlds.
- **Microsoft.Playwright.NUnit / xUnit test-helper packages as
  transitive deps (rejected).** The satellite ships only
  `Microsoft.Playwright`. Users writing tests with Playwright pull the
  test-helper package themselves. Keeps the consumer graph minimal.

## Accepted cost

- **Examples and integration-tests migrate in the same PR.** Two example
  apps, three test files touched. Mechanical work — `.WithPuppeteerPageLoader()`
  → `.WithPlaywrightPageLoader()` plus one `using` swap.
- **First-run downloads Playwright browsers via `playwright install`.**
  Microsoft.Playwright's standard first-run step downloads Chromium (and
  Firefox/WebKit if the consumer enabled them). Same shape as the
  PuppeteerSharp Chromium-provisioning path it replaces; documented in
  the satellite's README.
- **No PageAction-dispatcher abstraction yet** (ADR-0052 §Accepted cost
  flagged this; this ADR preserves the duplication). Two dispatch tables —
  CDP-direct and Playwright — both implementing the same seven arms.
  Extraction to a shared dispatcher is a future ADR once the pattern has
  stabilised across two adapters.
- **Microsoft.Playwright trim warnings.** The satellite is not AOT-clean
  in isolation; that is the ADR-0009 satellite-quarantine pattern working
  exactly as designed. Core's AOT smoke test does not pull this satellite;
  the CLI (ADR-0043) does not bake it. JIT consumers of the satellite are
  unaffected.

## Deliberate consequences

- **The Dynamic-slot picker UX is now genuine.** Through v11 a consumer
  picks between three transport satellites: the existing HTTP transport
  in core (Static slot), and for the Dynamic slot one of Playwright
  (`WebReaper.Playwright`) or CDP-direct (`WebReaper.Cdp` — including all
  stealth backends that compose on top). The seam shape from ADR-0004 is
  unchanged; the *option set* widens.
- **The CLI gains a coherent browser story.** Per ADR-0055, the CLI bakes
  only `WebReaper.Cdp` (AOT-clean); the CLI's `--browser` flag auto-spawns
  a managed Chromium and connects via CDP. Users who want Playwright
  per-transport features pick it explicitly at the library level. Two
  distinct surfaces, both supported, no ambiguity.
- **`PuppeteerExtraSharp` is dropped from the consumer graph.** The
  `WebReaper.Puppeteer` satellite pulled it; nothing in v11 does. Any
  consumer using the `PuppeteerExtraSharp`-based stealth plugins through
  the deleted satellite migrates to `WebReaper.Stealth.CloakBrowser`
  (ADR-0054) — a cleaner path than the puppeteer-extra plugin ecosystem.

## SemVer

**Major (11.0.0).** Public surface — the entire `WebReaper.Puppeteer`
package — is deleted. Clean cut, no compat shell, no deprecation window.
Announced via this ADR, CHANGELOG migration section, and the `README`
upgrade guide. Consistent with ADR-0009's exact precedent ("the project's
'called out loud, never silent' rule even though it does not use the
compat-shell mechanism").
