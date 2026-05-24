# Stealth-backend pattern: per-fork `WebReaper.Stealth.X` satellites composing on `WebReaper.Cdp`; `WebReaper.Stealth.CloakBrowser` ships first

## Status

**Proposed** (2026-05-24). Slice 3 of 4 in the **Transports wave** (v11.0.0
target). New satellite per ADR-0009 — `WebReaper.Stealth.CloakBrowser`
ships as the first concrete backend. The recipe other stealth Chromium
forks follow (Patchright, Camoufox, undetected-chromedriver, …) is
documented here so community-contributed satellites land on the same
shape without each one needing its own ADR.

## Context

The transports wave is structurally three layers:

1. **ADR-0052 — `WebReaper.Cdp`**: the CDP-direct transport + the public
   `CdpLaunchHelpers` utility. The bedrock for **Browser backend** swaps.
2. **ADR-0053 — `WebReaper.Playwright`**: the modern SDK-shaped browser
   transport; not relevant to stealth (Microsoft.Playwright doesn't bind
   stealth Chromium forks).
3. **This ADR** — the per-backend satellites that compose `CdpLaunchHelpers`
   + per-fork details + `WithCdpPageLoader(url)` wiring to give consumers
   a one-liner: `.WithCloakBrowser()` and equivalent.

Stealth scraping is the use case the current `WebReaper.Puppeteer` satellite
fundamentally cannot serve. PuppeteerSharp drives vanilla Chromium; vanilla
Chromium fails Cloudflare, reCAPTCHA v3, DataDome, FingerprintJS — every
mainstream invisible-bot-check on every site that matters. The whole point
of stealth Chromium forks like CloakBrowser is that **the fingerprint
patches live at the C++ level** — there's no JS-level "stealth plugin" that
catches up. The wave's transport split makes those forks first-class
citizens.

The user-convenience analysis (HITL Round 1, F4 + Stealth UX): the original
walked-through flow required users to `pip install cloakbrowser` and manage
a sidecar process. That cross-ecosystem friction killed the casual
audiobook-scraping scenario the wave was designed for. The redesign:
each `WebReaper.Stealth.X` satellite handles the fork's binary lifecycle
itself, downloading from upstream on first use (same legal model as
`playwright install`, `winget`, `brew install --cask`), and exposes a
one-line builder extension.

## Decision

### The pattern (the part future satellites mirror)

A `WebReaper.Stealth.X` satellite consists of three pieces:

1. **An `XLauncher` class** — finds the fork's binary on disk, optionally
   downloads it from upstream, launches it with the fork's recommended
   flags, returns a `LaunchedCdpEndpoint` (CDP URL + a `IAsyncDisposable`
   teardown handle). Built on `WebReaper.Cdp.CdpLaunchHelpers`.
2. **An `XInstaller` class** — `EnsureInstalledAsync(InstallOptions ct)`:
   detect pre-installed binary at PATH and well-known paths; if absent,
   download the upstream release artefact for the current RID
   (`win-x64` / `linux-x64` / `osx-x64` / `osx-arm64` / …) with checksum
   verification + resumable retry; cache under
   `~/.webreaper/stealth/<fork-name>/`. Idempotent — no-op if the binary
   is already cached.
3. **A `WithXBackend()` extension method** on `ScraperEngineBuilder`,
   living in `XBackendBuilderExtensions`. The body is exactly:

   ```csharp
   public static ScraperEngineBuilder WithCloakBrowser(
       this ScraperEngineBuilder b,
       CloakBrowserOptions? opts = null)
   {
       opts ??= new CloakBrowserOptions();
       var path = CloakBrowserInstaller.EnsureInstalledAsync(opts.InstallOptions).GetAwaiter().GetResult();
       var endpoint = CloakBrowserLauncher.LaunchAsync(path, opts).GetAwaiter().GetResult();
       return b.WithCdpPageLoader(endpoint.CdpUrl);
       // endpoint.DisposeAsync wired into the engine's teardown hook
   }
   ```

   Sync-over-async at the builder boundary is consistent with the rest of
   the builder surface; the work itself is async-internal.

No shared `IStealthBackend` interface, no abstract base class. The
*convention* is the spec — same packaging principle the existing data-store
satellites follow (`WriteToMongoDb`/`WriteToCosmos` are extensions, not
implementations of a shared `IDataStoreSatellite`).

### The first concrete satellite — `WebReaper.Stealth.CloakBrowser`

The CloakBrowser fork (v0.3.30 at draft-time, GitHub.com/CloakHQ/CloakBrowser,
20.1k stars, active May 2026) handles invisible bot-checks via C++-level
fingerprint patches:

| Challenge | Handled |
|---|---|
| Cloudflare Turnstile, reCAPTCHA v3, FingerprintJS, BrowserScan | Silent pass via fingerprint patches |
| reCAPTCHA v2 image grid, hCaptcha (interactive puzzles) | Not handled — separate concern, deferred to future captcha-solver wave |
| DataDome on aggressive sites | Partial — vendor docs recommend headed mode + residential proxies (already wired via `IProxyProvider`) |

**Launch shape:** `CloakBrowserLauncher.LaunchAsync(binaryPath, options)`
spawns the binary with `--remote-debugging-port=0` plus the
CloakBrowser-specific flags the vendor documents (`--user-data-dir`,
`--disable-background-networking`, `--no-first-run`, …), waits for the
port to be readable via `CdpLaunchHelpers.LaunchAsync`, returns the
endpoint.

**Install shape:** `CloakBrowserInstaller.EnsureInstalledAsync` reads the
latest release manifest from CloakHQ's GitHub Releases API (or a pinned
version when the consumer passes `InstallOptions.Version`), selects the
artefact matching the current RID, downloads it to
`~/.webreaper/stealth/cloakbrowser/<version>/`, verifies the SHA-256
checksum the release manifest carries, untars/unzips, marks the binary
executable on Unix. Resumable on partial download (HTTP `Range`).

### License acknowledgment — two surfaces

CloakBrowser's `BINARY-LICENSE.md` permits **use** but forbids
**redistribution**. The legal model the satellite uses is identical to
`playwright install` / `winget` / `brew install --cask`: WebReaper's CLI
and library code is a *download mechanism on behalf of the user*, not a
distributor. The binary is fetched from CloakHQ's own servers on the
user's machine; nothing is rehosted.

Two surface-level acknowledgments:

- **Library surface (`.WithCloakBrowser()`).** On first-run install, the
  installer **logs once** (via the wired `ILogger`) the license URL and
  a one-line reminder ("By using CloakBrowser, you accept its binary
  license terms — see https://github.com/CloakHQ/CloakBrowser/blob/main/BINARY-LICENSE.md").
  No interactive prompt — would break headless library / CI scenarios.
  Subsequent runs are silent (cached install).

- **CLI surface (`webreaper stealth install`).** Interactive Y/n prompt on
  the first install run that includes the license URL, install size, and
  upstream source. Unattended use bypasses the prompt via `--yes` flag or
  `WEBREAPER_AUTO_STEALTH=1` env var. See ADR-0055 for the full UX
  contract.

### Composition with existing seams

- **`IProxyProvider`** — passed through the ADR-0050 4-arg factory; the
  CloakBrowser launcher adds `--proxy-server=<url>` to the spawn args.
  Headed-mode + residential-proxy is the vendor-recommended config for
  the hardest sites (DataDome); the satellite supports both via
  `CloakBrowserOptions.Headed` + the user's `WithProxy(...)` registration.
- **`ICookiesStorage`** — handled by `CdpPageLoadTransport` itself (per
  ADR-0052), not the satellite. The satellite is launch-only; once the
  transport is wired the cookie projection runs through CDP.
- **`IActionResolver`** (ADR-0050) — same path; the transport receives the
  resolver, the satellite is invisible to it.
- **`WebReaper.AI` `LlmActionResolver`** — composes naturally:
  `.WithCloakBrowser().WithLlmActionResolver(chatClient)` is the
  "stealth + semantic actions" pair that handles "click the obscured
  download button on this protected page".

### Recipe for community-contributed stealth satellites

To add `WebReaper.Stealth.Patchright` (or Camoufox, undetected-chromedriver
Chromium fork, …):

1. New satellite project; `<PackageReference Include="WebReaper.Cdp" />`.
2. `PatchrightInstaller` calling Patchright's release-manifest endpoint;
   cache under `~/.webreaper/stealth/patchright/`; verify checksum.
3. `PatchrightLauncher` calling `CdpLaunchHelpers.LaunchAsync` with
   Patchright's documented flag set.
4. `WithPatchright()` extension on `ScraperEngineBuilder`, body identical
   in shape to `WithCloakBrowser()`.
5. A satellite-specific `BINARY-LICENSE` acknowledgment in the README
   plus the one-line `ILogger` log on first install.
6. Submit PR to `WebReaper.Cli`'s `KnownStealthBackends` registry (per
   ADR-0055) to opt the new satellite into the CLI's `webreaper stealth install`
   UX. Library use does not require the CLI registration.

Three classes + one extension + one README + one one-line PR to a
hardcoded list. The pattern is shallow because the seam beneath it
(`CdpLaunchHelpers` + `IPageLoadTransport`) is deep.

## Considered options

- **Per-backend satellites, shared utils in `WebReaper.Cdp`, convention
  pattern (chosen).** Matches ADR-0009 per-technology packaging principle;
  license isolation per fork; independent release cadence; zero dead code
  in a user's binary when they only want one backend; `dotnet add package`
  cost identical to umbrella.
- **Umbrella `WebReaper.Stealth` satellite with N extension methods
  (rejected).** Originally recommended in HITL Round 1, reversed during
  grilling. License-muddling across forks; coupled release cadence
  (CloakBrowser ships fast, Patchright may lag); dead-code for users who
  only want one backend. NuGet discoverability — the umbrella's one
  legitimate win — is solvable with README/docs.
- **No satellite, documented pattern only (rejected).** Lean toward user
  convenience was an explicit user constraint. A pattern + example
  doc forces every user to write the same installer + launcher boilerplate;
  the satellite-per-fork shape pays the cost once on our side and saves
  every consumer N hours of integration work.
- **`IStealthBackend` shared interface + DI-registered backends
  (rejected).** New abstraction earning no rent in v11; doesn't match
  existing satellite ecosystem; over-engineered. Consumers don't pick a
  backend from a list at runtime — they pick at compile time by which
  `WithXBackend()` extension they call. Future ADR may extract if a
  legitimate runtime-selection use case appears.
- **Bundle CloakBrowser binary in the NuGet package (rejected — legally
  blocked).** CloakBrowser's `BINARY-LICENSE.md` explicitly forbids
  redistribution. Even if it didn't, the binary is 220 MB per RID — six
  RIDs would push a 1.3 GB NuGet package, dramatically outside the
  community-acceptable size range.
- **Auto-escalation in the library (vanilla Chrome → bot-check detected →
  swap to CloakBrowser) (rejected — deferred).** This is the
  `TransportRouter` pattern (F7 in Round 1 grilling), deferred to v12
  pending demand. v11 library users pick a backend at build time;
  the CLI surface (ADR-0055) implements escalation as a CLI-only feature.

## Accepted cost

- **One ADR-by-PR per CLI-integrated stealth backend.** A satellite ships
  freely for library use; CLI integration via the
  `KnownStealthBackends` curated list requires a small PR per backend.
  Bottleneck on us merging; trade for the curated-quality bar on the CLI
  surface.
- **Cross-platform installer logic per fork.** Six RIDs × N forks × release-
  manifest formats per upstream. Mitigation: `CdpLaunchHelpers` and a
  forthcoming `StealthInstallerBase` (not in v11, planned for v12 once
  the second satellite lands and the actual shared code is visible)
  carry the cross-cutting work; per-fork code is the per-fork details.
- **License-acknowledgment surface is per-satellite.** Each
  `WebReaper.Stealth.X` README + first-run logger line names that fork's
  binary license. Mechanical work; the precedent this ADR sets is what
  future satellites copy.
- **The audiobook scenario "just works" claim has an asterisk.** Most
  trackers use Cloudflare or reCAPTCHA v3 — CloakBrowser handles those
  silently. Trackers using interactive captchas (reCAPTCHA v2 image
  grid, hCaptcha) still require an external captcha-solving service;
  the v12+ captcha wave addresses that.

## Deliberate consequences

- **Stealth becomes a one-liner.** `.WithCloakBrowser()` replaces the
  pre-wave alternative (no first-class stealth; users wired
  PuppeteerExtraSharp plugins manually or fell back to a sidecar Python
  process). The library positioning sharpens — "the .NET scraper that
  ships first-class stealth backends".
- **The `WebReaper.Stealth.*` namespace becomes a community-contribution
  on-ramp.** ADR-0009's "core stays dependency-light; satellites are the
  growth surface" extended to a new category. Each fork is one PR-ready
  pattern away from a NuGet listing.
- **CLI auto-escalation has a coherent target.** Per ADR-0055's Hybrid C
  UX, `webreaper scrape ... --browser` detects a bot-check, prompts the
  user to download CloakBrowser, runs `webreaper stealth install`,
  retries. The "what backend does --stealth install" question has one
  answer (CloakBrowser is the v11 default; community satellites added
  via the registry PR over time).

## SemVer

**Minor (additive).** New satellite package in the lockstep wave; no core
surface change. v11.0.0's major is owned by ADR-0053's Puppeteer deletion.
