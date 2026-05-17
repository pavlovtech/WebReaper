# The builder is a public registration seam; heavy adapters are per-technology satellite packages, the core is dependency-light

Every third-party SDK is already confined to a single Concrete adapter
behind a clean seam — `StackExchange.Redis` in 5 files, `MongoDB.Driver` in
2, `Microsoft.Azure.Cosmos` in 1, `Azure.Messaging.ServiceBus` in 1,
`PuppeteerSharp`/`PuppeteerExtraSharp` in 2. Those seams are **deep**: ADR
0003 (keyed blob store), ADR 0004 (one page-loader/transport seam), and ADR
0008 (the typed pipeline) already did that interface work. The seams are not
the problem.

The problem is the builder. `ScraperEngineBuilder` statically `new`s every
concrete adapter through ~11 `WriteToX`/`WithX` methods —
`WriteToCosmosDb` → `new CosmosSink` (ScraperEngineBuilder.cs:168),
`WriteToMongoDb` → `new MongoDbSink` (:180), `WithRedisScheduler` → `new
RedisScheduler` (:306), `WithAzureServiceBusScheduler` → `new
AzureServiceBusScheduler` (:288), and the Redis/Mongo cookie- and
config-storage variants. Because the builder names every adapter by concrete
type, **every adapter's `PackageReference` is forced into the core
assembly**. A consumer doing a plain HTTP → JSON-lines crawl transitively
pulls `Microsoft.Azure.Cosmos` (→ Newtonsoft + native
`Microsoft.Azure.Cosmos.ServiceInterop.dll`/`vcruntime140*.dll`),
`MongoDB.Driver` (→ `SharpCompress`, the GHSA-6c8g-7p36-r338 the core
`.csproj` suppresses), `StackExchange.Redis`, `Azure.Messaging.ServiceBus`,
and `PuppeteerSharp` (→ a ~Chromium provisioning path).

Deletion test: drop the Cosmos `PackageReference`. `CosmosSink` and the one
builder method that constructs it stop compiling; nothing else in core does.
The dependency concentrates **zero** core complexity — it is optional
surface welded onto core through the builder. Identical result for Mongo,
Redis, Azure Service Bus, and Puppeteer individually. ADR 0008 named this
exact follow-up ("CosmosSink satellite-package split — Needs its own ADR");
the finding here is that the shape is **general** — five SDKs, one
mechanism — and the lever is the builder, not the seam.

## Why this is worth a major break

Three concrete, technical payoffs, not a positioning bet:

1. **Default-graph weight.** A core-only consumer sheds five SDK graphs,
   including a native-interop one (Cosmos `ServiceInterop`/`vcruntime`), a
   suppressed-CVE transitive (`SharpCompress` via Mongo), and the Chromium
   provisioning path (Puppeteer). These are properties of the dependency
   graph, independent of how the library is positioned.
2. **The builder deepens.** It stops being a shallow ~25-method surface
   whose implementation is ~1:1 `new ConcreteAdapter(...)` and becomes a
   small **Registration seam** (CONTEXT.md "Wiring") over the role
   interfaces — high leverage across every adapter *and* every consumer's
   own. The satellite split is the *consequence* of the deepening, not a
   packaging chore bolted beside it.
3. **Puppeteer becomes removable as a package-drop**, not core surgery — a
   stated 2026 direction (headless-browser scraping is uncommon), recorded
   here, deliberately not actioned here.

## Decision

- **The builder is a public Registration seam.** Public methods over the
  *existing* role interfaces: `AddSink(IScraperSink)`,
  `WithScheduler(IScheduler)`, `WithLinkTracker(IVisitedLinkTracker)`,
  `WithConfigStorage(IScraperConfigStorage)`,
  `WithCookieStorage(ICookiesStorage)`, `WithContentParser(IJsonContentParser)`,
  plus a new `WithLoadTransport(IPageLoadTransport)`. **Verified, not
  inferred: five of these six already exist on `ScraperEngineBuilder`** —
  `AddSink` (:69), `WithScheduler` (:282), `WithLinkTracker` (:110),
  `WithConfigStorage` (:340), `WithCookieStorage` (:315),
  `WithContentParser` (:42). Only the load-transport registration is new;
  the browser transport is currently buried as a `SpiderBuilder.Build()`
  default. This deepening is therefore **mostly deletion plus one new
  method**, not new interface design.
- **Heavy adapters become Satellite adapters, packaged per technology**:
  `WebReaper.Cosmos`, `WebReaper.Mongo`, `WebReaper.Redis`,
  `WebReaper.AzureServiceBus`, `WebReaper.Puppeteer`. Per technology because
  the cost being shed is the SDK and SDKs are per-technology; per-seam
  packaging would multiply packages without shedding additional weight and
  would orphan ADR 0005's shared `RedisConnectionPool`, which instead stays
  whole inside `WebReaper.Redis` with the four Redis adapters — the ADR 0005
  one-multiplexer-per-connection-string invariant becomes intra-package and
  is preserved.
- **Each satellite ships its `WriteToX`/`WithX` convenience as extension
  methods on `ScraperEngineBuilder`** over the public seam (`this
  ScraperEngineBuilder b … b.AddSink(new CosmosSink(...))`). Caller
  ergonomics are preserved (`.WriteToCosmosDb(conn)` still chains) at the
  cost of one `using WebReaper.Cosmos` plus a package reference — opt-in is
  correct.
- **Clean cut, no compat shell.** The ~11 concrete `WriteToX`/`WithX`
  methods are deleted from core outright in this major; there are no
  `[Obsolete]` forwarders. See SemVer for why this deliberately departs from
  the project's staged-compat precedent.
- **`SpiderBuilder` becomes `internal`** and loses its public adapter
  sugar. It is already a private collaborator of `ScraperEngineBuilder`
  (ScraperEngineBuilder.cs:34); its public `WriteToX` surface was duplicate.
  The Registration seam lives only on `ScraperEngineBuilder`.
- **The default page loader is HTTP-only.** The browser transport moves to
  `WebReaper.Puppeteer` and is wired via `WithLoadTransport`;
  `GetWithBrowser()` / dynamic pages become opt-in. ADR 0004's one
  `IPageLoader` / two `IPageLoadTransport` seam is unchanged — only the
  default *composition* moves out and a public registration method is added.

## Staging (tracer slices; the guardrail suite stays green at each)

1. **Cosmos first.** `WebReaper.Cosmos`: move `CosmosSink`, ship
   `WriteToCosmosDb` as an extension method, delete the core methods, drop
   `Microsoft.Azure.Cosmos` — and with it the Cosmos→Newtonsoft+native
   `ServiceInterop` graph — from core. Smallest slice, single file, no
   default-path wrinkle; the ADR-0008-named adapter and the worst graph.
   Proves the extension-method mechanism end to end.
2. **Puppeteer.** `WebReaper.Puppeteer`: `BrowserPageLoadTransport` (and
   the Puppeteer half of `CookieExtensions`), the new `WithLoadTransport`
   seam, the core default flips HTTP-only. Carries the ADR-0008-named
   `BrowserPageLoadTransport` `Assembly.Location` IL3000 finding out of core
   with it.
3. **Redis / Mongo / Azure Service Bus**, mechanically, one package each,
   on the proven pattern. `RedisConnectionPool` travels whole into
   `WebReaper.Redis`.

Each slice keeps the guardrail green: the unit suite, the whole-solution
build (Examples included), and `WebReaper.AotSmokeTest`. Examples that use a
satellited adapter (`WebReaper.AzureFuncs`,
`WebReaper.DistributedScraperWorkerService`, …) gain the satellite
`PackageReference` in the same slice — the whole-solution CI build is the
guardrail that this stays consistent.

## Bounded scope — what this does NOT change

- **Newtonsoft does not leave core.** `JsonSchemaBackend` /
  `JsonContentParser`'s JSONPath `JToken` cursor and `CookieStore`'s
  `CookieContainer` quirk still reference Newtonsoft — the ADR-0008 named
  follow-ups, untouched here. Removing `CosmosSink` removes the
  *Cosmos→Newtonsoft* reachability axis, **not** the core Newtonsoft
  `PackageReference`; per ADR 0008 the JSON backend already makes Newtonsoft
  AOT-reachable independent of Cosmos. This ADR makes the default consumer
  graph dramatically lighter and removes the native-interop / CVE / Chromium
  transitive deps; a zero-warning core `PublishAot` remains **gated on ADR
  0008's named JSONPath-for-STJ migration**. Necessary surface reduction,
  not the AOT gate itself — the ADR 0008 "necessary but not sufficient"
  posture, restated.
- **The seam interfaces.** `IScraperSink`, `IScheduler`,
  `IKeyedBlobStore`, `ICookiesStorage`, `IVisitedLinkTracker`,
  `ISchemaBackend<TNode>`, `IPageLoader`/`IPageLoadTransport` are unchanged.
  Only the builder's registration entry points and the adapters' packaging
  change.
- **ADR 0003 keyed blob store + payload shells.** The seam,
  `ScraperConfigStore`, and `CookieStore` stay in core; only the
  Redis/Mongo blob-store adapters and the thin Redis/Mongo
  config/cookie-storage wrappers relocate. In-memory/file adapters stay.
- **ADR 0001/0002/0004/0005/0006 mechanisms.** The closed `CrawlOutcome`
  sum, the Schema fold's one home, the one loader/transport seam, the
  `RedisConnectionPool` invariant (now intra-`WebReaper.Redis`), and the
  buffered file-sink drain are all untouched.
- **Puppeteer removal.** Satelliting it makes a future removal a
  package-drop; the removal itself is a stated direction, explicitly not
  actioned in this ADR — the ADR 0004/0005 "named, distinct future
  candidate" posture.

## Deliberate consequences (see CONTEXT.md "Wiring" / "Flagged ambiguities")

- **The two-builder `WriteToX` duplication is closed by construction.**
  `ScraperEngineBuilder` and the public `SpiderBuilder` both carried the
  same adapter sugar; `SpiderBuilder` becomes `internal` and the sugar
  exists once, in satellites — a pre-existing shallow-surface smell removed,
  the ADR 0003/0004/0006 "duplication closed, not relocated" posture.
- **Consumer-supplied adapters become a first-class feature.** A public
  Registration seam makes `.AddSink(myCustomSink)`,
  `.WithScheduler(myScheduler)`, `.WithLoadTransport(myTransport)`
  supported extensibility rather than an accident — latent capability made
  real.
- **The clean cut breaks existing consumer code at the call site.**
  `WriteToCosmosDb`/`WriteToRedis`/… stop resolving until the consumer adds
  the satellite package and `using`. Announced via this ADR and the
  CHANGELOG migration section; not silent.

## SemVer

**Major.** Public surface is removed outright — the ~11 concrete builder
methods, `SpiderBuilder`'s public adapter surface, and `SpiderBuilder`'s
accessibility — with no deprecation window. This is a **deliberate
departure from precedent**: ADR 0002/0003/0004/0008 each isolated a major
break behind an additive compat shell with an announced removal. That
posture is **inapplicable here by construction** — a compat forwarder for
`WriteToCosmosDb` must still `new CosmosSink`, so core must still reference
`Microsoft.Azure.Cosmos`, so the dependency-light core (the entire
load-bearing reason for the work) is not delivered until the forwarder is
later removed. Staging would defer the only payoff by a full major. The
break is therefore taken in one move and **announced** (this ADR +
CHANGELOG) — consistent with the project's "called out loud, never silent"
rule even though it does not use the compat-shell mechanism.

## Considered options

- **Public Registration seam on `ScraperEngineBuilder` + per-technology
  satellites + clean cut + `SpiderBuilder` internal (chosen).** Five of six
  seam methods already exist; the change is mostly deletion plus one
  `WithLoadTransport`. Delivers the dependency-light core immediately,
  deepens the builder, closes the two-builder duplication, preserves every
  prior ADR seam.
- **`internal` + `[InternalsVisibleTo]` registration (rejected).** Forces
  core's `.csproj` to name every satellite assembly — a hardcoded satellite
  registry inside core, recreating the exact core→adapter coupling the split
  removes, and closing the model to third-party adapters.
- **Per-seam packaging (rejected).** ~13 packages; sheds no SDK weight
  beyond per-technology (the SDK *is* per-technology); orphans ADR 0005's
  shared `RedisConnectionPool` into a common package the per-seam Redis
  packages must re-depend on — re-coupling what ADR 0005 unified.
- **Staged compat forwarders (rejected).** Keeps every SDK referenced by
  core for another major; the dependency-light / footprint payoff — the
  whole point — is not delivered until removal. Strictly defers the only
  benefit.
- **Extension methods on both `ScraperEngineBuilder` and a public
  `SpiderBuilder` (rejected).** Doubles satellite surface and preserves the
  pre-existing duplication the deepening exists to remove. A half-measure.
- **Keep the browser transport in core (rejected).** `PuppeteerSharp` + the
  Chromium provisioning path + the IL3000 finding are the single heaviest
  dependency; leaving it in core makes "dependency-light core" materially
  false and forecloses the stated Puppeteer-removal direction.
