# The core public surface is the contract — drawn by the deletion test, documented to the bar, the rest made internal, CS1591 ratcheted to zero

## Status

**Accepted — shipped in 9.0.0** (2026-05-19; design pass + all six staging
slices landed and published in the lockstep 9.0.0 wave — see *Implementation
status* at the end). The one fork flagged for grilling — the disposition of
the in-memory default adapters — was resolved in the grilling loop in favour
of the principled line below (see *Considered options → the
in-memory-defaults sub-decision*).

## Context

Six satellite csprojs carry a verbatim, load-bearing rationale:

> *Ship the XML doc so the documented builder-extension API (the satellite's
> supported surface) gives consumers IntelliSense. The adapter classes … are
> public only because the extension and tests bind to them; they are not part
> of the satellite's documented contract, so CS1591 for them is noise, not a
> backlog signal. **Core keeps CS1591 visible by contrast — there it IS a live
> doc backlog.***

That last sentence is a recorded decision: core CS1591 was deliberately left
*un-suppressed* so the backlog stays a visible signal. It has now been
measured — **~552 CS1591 across 65 core files** (~2× inflated by the build
listing each for project and solution; ≈276 declarations). This ADR is what
that decision was waiting for: it turns the standing backlog into a *closed,
enforced contract*, and updates the satellite sentence in place to say so.

The public surface is an **interface** in the LANGUAGE.md sense — *everything
a caller must know to use the module*. Measured against that definition it is
**under-documented where it is contract** and **accidentally wide where it is
not**:

- **The fluent front door ships with no IntelliSense.** `ConfigBuilder.Follow`
  / `.Paginate` / `.Build`, the `ScraperEngineBuilder` façade — the exact API
  the README *teaches* — have zero XML doc (~56 declarations in `Builders/`).
  The interface is advertised in prose and absent at the call site.
- **The surface is accidentally wide.** `SpiderBuilder` is already `internal`
  (ADR-0009) — precedent that "public" here often meant "a test or satellite
  binds to it", not "a consumer calls it".

### The line is the deletion test, not the folder

The error to avoid is drawing the Tier line as `Abstract`-public vs
`Concrete`-internal — optimising for "fewest public concretes". That is the
wrong target. The right target: **the public surface should *be* the contract
— no wider, no narrower.** Applied per type via the deletion test (evidence:
a repo-wide reference sweep, who-names-what across satellites / Examples /
Misc / tests):

- **`new InMemoryScraperConfigStorage()`** — `Examples/WebReaper.AzureFuncs/
  Startup.cs:36`. The ADR-0009 "build your own distributed worker" pattern
  (which deliberately bypasses `ScraperEngineBuilder` via `BuildSpider()`)
  wires an in-memory store *by hand*. Delete this from the public surface and
  the complexity reappears in **every** DIY-distributed consumer — each
  hand-rolls a trivial `IScraperConfigStorage`. It is *earning its keep* as a
  contract adapter at a real, many-adapter seam. → **public + documented.**
- **`CookieStore`, `ScraperConfigStore`** — under `*/Concrete/` paths, but
  shipped satellites *inherit* them (`RedisCookieStorage : CookieStore`,
  `MongoDbScraperConfigStorage : ScraperConfigStore`, …). They are the
  ADR-0003 payload-shell extension points: an inheritance contract an
  extension author subclasses. → **public + documented (the best docs — they
  are subclassed across assemblies).**
- **`LoggerExtensions.LogMethodDuration()`** — called by
  `WebReaper.Puppeteer/BrowserPageLoadTransport.cs:56` (shipped satellite
  prod). A satellite binds it. → **public + documented.**
- **`new ConsoleSink()`, `Executor.RetryAsync`, `FileScheduler`,
  `ColorConsoleLogger`, the `File*`/parser/sink/page-loader leaves** — named
  by **no** consumer, satellite, or Example; reached only through a fluent
  builder method (`.WriteToConsole()`, `.WithTextFileScheduler(...)`) or
  core-internally. Delete them from the *public* surface and nothing reappears
  anywhere. Zero leverage lost. → **internal.**

A factory introduced solely to vend `new InMemoryX()` (the alternative for
keeping the surface "concrete-free") is a **shallow module**: its interface is
as complex as its implementation, behaviourless indirection. Deletion test on
*that* factory: delete it, complexity vanishes — a pass-through. Rejected (see
*Considered options*).

### The failure mode this ADR exists to prevent

**Mechanically generating filler comments to zero the counter.** A doc that
restates the signature is worse than an honest gap — it destroys the very
signal the visible-backlog decision protected. The codebase already sets the
bar (the `ScraperEngine` / `HttpPageLoadTransport` summaries: intent,
invariants, the *why*, ADR-linked). Anything below that bar is not progress.
Internalising Tier-2 is the *non-filler* way to clear ~half the backlog: CS1591
does not fire on non-public members, so the warning vanishes with **no NoWarn
and no comment** when the type stops being contract.

## Decision

- **Draw the surface by the deletion test (the principle above).** Public iff
  named by a documented consumer / inherited by a satellite / part of the
  taught fluent API. Everything else is implementation.

- **Tier-1 — the contract: public, documented to the bar.**
  - `Builders/` — `ScraperEngineBuilder` (façade), `ConfigBuilder`,
    `PageActionBuilder`.
  - `*/Abstract/` — every pluggable seam interface (`IScheduler`,
    `IVisitedLinkTracker`, `IScraperConfigStorage`, `ICookiesStorage`,
    `IScraperSink`, `IPageLoader`/`IPageLoadTransport`, `ISpider`,
    `ICrawlStep`, `ILinkParser`, `IContentParser`, `IProxyProvider`,
    `IKeyedBlobStore`, `IOutstandingWorkLatch`, …).
  - Public `Domain/` model: `Schema`, `LinkPathSelector`, `PageAction` /
    `PageActionType`, `PageType`, `Job`, `ScraperConfig`, `ParsedData`,
    `Metadata`, `CrawlOutcome` (+ `Parsed`/`Followed`/`Paginated`),
    `JobReport`, `PageRequest`.
  - The payload-shell extension bases satellites inherit: `CookieStore`,
    `ScraperConfigStore`.
  - The in-memory default adapters the DIY-distributed pattern constructs by
    name: `InMemoryScheduler`, `InMemoryScraperConfigStorage`,
    `InMemoryVisitedLinkTracker`.
  - `WebReaperJson` (the ADR-0008 cross-assembly Job/Config grammar; satellite
    schedulers serialise through it).
  - `LoggerExtensions` (a shipped satellite binds it).
  - Public exceptions a consumer catches.

  Bar: purpose + the non-obvious (invariants, ordering, error modes, the
  *why*), governing ADR linked where one exists, density matching the
  `ScraperEngine`/`HttpPageLoadTransport` summaries. **A reviewer (human or AI)
  rejects any comment that only restates the signature.**

- **Tier-2 — implementation: made `internal`.** The `File*` leaves
  (`FileScheduler`, `FileScraperConfigStorage`, `FileCookieStorage`,
  `FileVisitedLinkedTracker`, `FileBlobStore`), `InMemoryBlobStore`, the sinks
  (`ConsoleSink`, `CsvFileSink`, `JsonLinesFileSink`), the parsers
  (`AngleSharpContentParser`, `JsonContentParser`, `XPathContentParser`), the
  page loaders, `InMemoryOutstandingWorkLatch`, `Infra/Executor`,
  `Logging/ColorConsoleLogger`. `[InternalsVisibleTo]` targets the **test
  assemblies only** — verified: no satellite/Example/Misc names a Tier-2 type;
  satellites bind core *interfaces* and inherit the *payload-shell bases*
  (both Tier-1). No shipped package is an `InternalsVisibleTo` target.

- **One release: 9.0.0 (Major).** The maintainer accepts the break. The
  surface-shrink is done in this initiative, not deferred — internalising
  Tier-2 is the breaking change and it ships with the docs, as one coherent
  major.

- **Enforcement: ratchet to zero, then project-wide.** As each Tier-1 area
  reaches zero CS1591, set `dotnet_diagnostic.CS1591.severity = error` for that
  path (path-scoped `.editorconfig`), monotone — a new undocumented public
  member there fails the build. End state (Tier-1 documented + Tier-2 internal
  ⇒ core CS1591 == 0): promote to project-wide `WarningsAsErrors=CS1591`; the
  scoped `.editorconfig` sections are then redundant scaffolding and removed.
  No scoped-NoWarn device is needed at the end state — internalisation, not
  suppression, clears Tier-2.

- **Close the loop on the recorded decision.** The satellite-csproj sentence
  *"Core keeps CS1591 visible by contrast — there it IS a live doc backlog"*
  is updated, in place, to point at this ADR: core CS1591 is now a
  contract-enforced surface, not an open backlog.

## Staging (slices; guardrail green at each — offline unit suite, whole-solution build incl. Examples/Misc, `WebReaper.AotSmokeTest`)

1. **`*/Abstract` seam interfaces** — highest leverage (an extension author's
   entire surface; the offline test surface). Document to the bar; ratchet
   those directories → error.
2. **`Builders/` fluent API** — the README's front door. Document; ratchet.
3. **Public `Domain/` model** + the payload-shell bases (`CookieStore`,
   `ScraperConfigStore`) + the in-memory default adapters. Document; ratchet.
4. **`WebReaperJson` + `LoggerExtensions` + public exceptions.** Document;
   ratchet.
5. **Internalise Tier-2** + `[InternalsVisibleTo]` for the test assemblies.
   Verify whole-solution build clean; no satellite/Example breaks (none names
   a Tier-2 type — `InMemoryScraperConfigStorage` stayed Tier-1, so AzureFuncs
   is untouched). Promote to project-wide `WarningsAsErrors=CS1591`; remove the
   scaffolding `.editorconfig` sections. Update the satellite-csproj rationale
   sentence. **Core CS1591 == 0, enforced.**

Each slice is a separate commit; Phase A/B is *not* split — the break
(internalisation) is slice 5 of the one 9.0.0 initiative.

## Bounded scope — what this does NOT change

- **No behaviour change.** XML doc + visibility modifiers + csproj/
  `.editorconfig` only. Internalising a type is an IL visibility change, not a
  logic change; the guardrail (unit suite + whole-solution build + AOT smoke)
  proves the composition still builds and runs.
- **No signature change to Tier-1.** The contract a consumer uses is
  byte-identical; it only gains documentation and enforcement.
- **Satellite csproj CS1591 NoWarn** — unchanged; only core's posture changes
  and only its rationale sentence is updated to cross-reference this ADR.
- **ADR-0009's registration seam / `BuildSpider()` / `internal SpiderBuilder`**
  — unchanged. This ADR documents and tightens the surface ADR-0009 defined;
  it does not move the seam. The DIY-distributed pattern keeps its in-memory
  building blocks (Tier-1 by the deletion test).
- **The seam interfaces' identity (ADR-0001/0002/0003/0004/0022).** Untouched;
  they are *documented*, not reshaped.

## SemVer

**Major — 9.0.0.** Tier-2 types leave the public surface (`public` →
`internal`). Announced via this ADR + the CHANGELOG migration section, never
silent (project rule). Evidence-backed blast radius: **no in-repo consumer and
no shipped satellite names a Tier-2 type** (repo-wide sweep); the only
externally-named concrete, `InMemoryScraperConfigStorage` (AzureFuncs), is
Tier-1 by the deletion test and stays public. The break is real (the types are
gone from the API) but affects only code that reached past the contract into
implementation — exactly what a major is for. Test assemblies retain access via
`[InternalsVisibleTo]`.

## Considered options

- **The deletion-test line: contract public + documented, implementation
  internal, one 9.0.0 (chosen).** The surface ends up *being* the contract;
  ~half the backlog clears by internalisation (no filler), the other half by
  real docs at the bar; enforcement makes it non-regressing.
- **Document everything to literal zero, keep it all public (rejected).**
  Filler on implementation types no one calls — most effort on least leverage,
  destroys the signal, doesn't shrink the surface.
- **Bulk `NoWarn CS1591` on core — the one-line "make the number go away"
  (rejected).** Reinstates exactly the invisible-backlog state the satellite
  rationale deliberately avoided; silences the front-door gap too.
- **The in-memory-defaults sub-decision (grilled; chosen = keep public +
  document).** Alternative B: internalise them and add a public factory seam
  for the DIY pattern. Rejected — the factory is a shallow module (interface
  as complex as its one-line implementation, behaviourless indirection; fails
  the deepening/deletion test). Alternative C: internalise and narrow the DIY
  pattern to distributed-store-only. Rejected — optimises "zero public
  concretes" at the cost of a documented capability (in-memory DIY wiring for
  local/test/single-node), with no architectural gain. A keeps the surface
  aligned with the real contract without inventing a shallow seam. Recorded so
  a future review does not re-suggest the factory.
- **Split into non-breaking 8.1.0 (docs) + breaking 9.0.0 (internalise)
  (rejected by the maintainer).** Considered to ship the non-breaking value
  first; the maintainer accepts the break, so the simpler single-major
  sequencing is taken (one coherent story, no scoped-NoWarn transitional
  device to carry and later remove).
- **Burn down, no enforcement (rejected).** The unenforced backlog is *how 552
  accumulated*; without the ratchet it silently re-accumulates.

## References

- The satellite csproj CS1591 rationale (the recorded decision this ADR
  revises and updates in place).
- ADR-0009 — the registration seam, `BuildSpider()`, `internal SpiderBuilder`,
  and the DIY-distributed pattern that shaped this surface.
- ADR-0003 — the payload-shell bases (`CookieStore`, `ScraperConfigStore`)
  satellites inherit; ADR-0008 — the `WebReaperJson` grammar satellites
  serialise through.
- LANGUAGE.md — *interface = everything a caller must know*; depth; shallow
  module; the deletion test (the framing for the Tier line and the
  factory rejection). CONTEXT.md — the domain vocabulary Tier-1 docs speak in.

## Implementation status — shipped (2026-05-19)

Landed on branch `adr-0023-core-doc-contract` (PR #76, merged), published in
the lockstep **9.0.0** release wave (core + six satellites; nuget.org
verified). Each slice kept the guardrail green (whole-solution build incl.
Examples/Misc + AOT smoke; the offline unit suite):

1. **Design pass** (`769d7e6`) — accepted after three grilling rounds.
2. **Slices 1–4** (`8100480` / `a6a1b91` / `8106dea` / `bf09243`) — the
   `*/Abstract` seam interfaces, the `Builders/` fluent API, the `Domain`
   model + payload-shell bases + in-memory defaults, then `WebReaperJson` +
   `LoggerExtensions` + the public exceptions: documented to the bar, each
   area ratcheted to `CS1591`-error via path-scoped `.editorconfig`.
3. **Slice 5a** (`1621fe1`) — the last Tier-1 the "remaining CS1591 == Tier-2"
   heuristic mis-classified: `ScraperEngine`, `StaticProxySource`,
   `HttpProxyValidator` (the deletion test overrides the heuristic).
4. **Slice 5b — the break** (`395879c`) — 27 Tier-2 implementation types made
   `internal` (+ `ScraperEngine`'s ctor); `[InternalsVisibleTo]` → the test +
   AOT-smoke assemblies only (never a shipped package); project-wide
   `WarningsAsErrors=CS1591`; the scaffolding `.editorconfig` sections
   removed; the six satellite csprojs' "live doc backlog" rationale updated
   in place to cite this ADR. A fourth Tier-1 the deep read caught —
   `SchemaContentParser<TNode>`, the ADR-0002 custom-backend reuse vehicle —
   was kept public + documented, not internalised.

Core `CS1591` went **294 → 0**, now contract-enforced (an undocumented public
member fails the build). Follow-up: the release version was single-sourced
into `Directory.Build.props` and `release.yml` switched to effective-`Version`
selection (PR #78); eliminating even that one manual bump is
[ADR-0024](0024-tag-derived-version.md) (proposed).
