# Changelog

## 8.0.0 — Crawl driver + Outstanding-work latch; the per-Job shell is a value, not a thrower (breaking)

The per-Job `Spider` shell stops leaking its result through side channels and
stops throwing to terminate. `ISpider.CrawlAsync` now returns a closed
`JobReport` (the ADR-0001 `CrawlOutcome` + the loaded document); the shell is
reduced to load → Crawl step → report. The **Crawl driver** — in-process
`ScraperEngine` or the distributed worker — owns what the shell used to: the
visited-link tracker, the crawl-limit stop, sink fan-out, and the
`PostProcessor` / `ScrapedData` callbacks. The crawl limit is now a value the
driver checks (`Scheduler.Complete()`), no longer a `PageCrawlLimitException`
thrown through the fault-retry policy. Rationale, the rejected alternatives
(durable-workflow coordinator, emergent queue-drain, exact distributed limit)
and the staged design:
[`docs/adr/0022-crawl-driver-and-outstanding-work-latch.md`](docs/adr/0022-crawl-driver-and-outstanding-work-latch.md)
and [`research/distributed-crawl-termination.md`](research/distributed-crawl-termination.md).

**Release packaging (lockstep).** This is a core-major release wave: core and
all six satellites (`Cosmos`, `Mongo`, `Redis`, `AzureServiceBus`, `Puppeteer`,
`Sqlite`) are republished at `8.0.0` together, so a consumer never sees a
`WebReaper 8.0.0` + satellite-`7.x` skew and every satellite declares a
`WebReaper >= 8.0.0` dependency. Only `WebReaper.Redis` changed functionally
(the distributed Outstanding-work latch + the atomic-`SADD` `TryAdd`); the
other satellites are unchanged, rebuilt against core 8.0.0 for graph
coherence (`docs/RELEASE-RUNBOOK.md` lockstep selection).

The visited-link tracker becomes the single **idempotency authority**:
`IVisitedLinkTracker.TryAddVisitedLinkAsync` is an atomic test-and-set
(default-interface-method; `InMemoryVisitedLinkTracker` is a lock-free CAS,
`RedisVisitedLinkTracker` an atomic `SADD`). Termination detection is one
`IOutstandingWorkLatch` seam — a unit-credit counter that trips exactly once
when all work has drained, with an in-memory `Interlocked` adapter and a
distributed Redis adapter (atomic `INCRBY`/`DECRBY` + a `SET NX` one-shot
fence). `Examples/WebReaper.AzureFuncs` is now a real distributed Crawl
driver: it never throws to terminate, so the queue is no longer poisoned at
the crawl-limit boundary.

Closed by construction: the retry-amplified limit exception, the racy
discovery dedup, and the distributed poison message. The fluent builder API
is unchanged — `.PageCrawlLimit(...)`, `.Subscribe(...)`, `.PostProcess(...)`
and `.StopWhenAllLinksProcessed()` keep their signatures and behaviour; only
their internal wiring moved to the driver.

**Build hygiene (non-breaking).** The obsolete, *inert* `ServicePointManager`
calls in the HTTP transport were removed — `ServicePointManager` does not
affect `HttpClient`/`SocketsHttpHandler`, and the connection limit and
cert-bypass already live on the per-request `SocketsHttpHandler`, so there is
no behavioural change. The broken/ambiguous XML-doc crefs and bad paramrefs
that shipped in the package's IntelliSense XML were fixed. The deliberately
visible core CS1591 doc backlog is intentionally left as-is (it is a tracked
signal, not noise — see the satellite csproj rationale).

### Breaking changes

- **`ISpider.CrawlAsync` returns `Task<JobReport>`**, not `Task<List<Job>>`.
  A direct `ISpider` consumer (the `BuildSpider()` distributed-worker pattern)
  reads `report.Outcome.NextJobs` for child jobs and matches
  `report.Outcome is CrawlOutcome.Parsed` for a parsed page; sink fan-out and
  visited-link tracking are now the caller's (driver's) job.
- **`ScrapedData` / `PostProcessor` moved off the concrete `Spider`** to the
  Crawl driver. The public `ScraperEngineBuilder.Subscribe(...)` /
  `.PostProcess(...)` surface is unchanged and now wires onto the driver.
- **`PageCrawlLimitException` is removed.** The crawl limit is a value-driven
  stop, never an exception; remove any `catch (PageCrawlLimitException)`.
- **`Spider`'s constructor changed** (reduced to `ICrawlStep`, `IPageLoader`,
  `IScraperConfigStorage`); `ScraperEngine`'s constructor gained the
  visited-link tracker, sinks, optional callbacks and an optional
  `IOutstandingWorkLatch` (in-memory default). Both are normally built via
  `ScraperEngineBuilder` — no change for fluent-API consumers.
- **No compat shell.** A `List<Job>`-returning forwarder would reinstate the
  side channels this change removes (the ADR-0009 precedent); the break is
  announced here and in ADR-0022, not silent.
- **Builder argument validation is now fail-fast.** Public builder entry
  points that take a free-form string reject `null`/empty/whitespace *at the
  call that introduced it* instead of failing much later (at parse time, at
  first file I/O, or — for start URLs — as an engine that quietly crawled
  nothing): `ConfigBuilder.Build()` now requires a non-empty start-URL set and
  its message is plural; `Follow`/`FollowWithBrowser`/`Paginate`/
  `PaginateWithBrowser` reject a blank selector; the file-backed
  `ScraperEngineBuilder` methods (`WriteToCsvFile`/`WriteToJsonFile`/
  `TrackVisitedLinksInFile`/`WithFileConfigStorage`/`WithFileCookieStorage`/
  `WithTextFileScheduler`) reject a blank path; and `PageActionBuilder`'s
  `Repeat`/`RepeatWithDelay`/`RepeatAndWaitForNetworkIdle` throw a clear
  `InvalidOperationException` when called before any action (was a bare
  `ArgumentOutOfRangeException`). Correct code is unaffected — only
  previously-invalid input now throws; a major is the right window for the
  few cases that were silently accepted before.

### Migration

A fluent-API consumer (`new ScraperEngineBuilder()…BuildAsync()` /
`.RunAsync()`) needs **no changes** — the builder surface and behaviour are
preserved. A direct `ISpider` / `BuildSpider()` consumer updates to the
`JobReport` shape (re-enqueue `report.Outcome.NextJobs`; fan a
`CrawlOutcome.Parsed` page out to its sink itself) and drops any
`PageCrawlLimitException` handling; the rewritten
`Examples/WebReaper.AzureFuncs` is the reference distributed Crawl-driver
adapter. The one behavioural caveat for a fluent consumer: builder method
*signatures* are unchanged, but a call that previously passed a blank
string / empty start-URL set (or `Repeat*` before any action) now throws at
that call — pass valid input.

## 7.1.0 — WebReaper.Sqlite satellite: opt-in robust-local durable scheduler & tracker (additive)

New satellite package **WebReaper.Sqlite** — a local durable scheduler and
visited-link tracker backed by an embedded SQLite store via
`Microsoft.Data.Sqlite`. "Resume" is a `SELECT … WHERE consumed = 0` over an
indexed table: `FileScheduler`'s append-only job file + sidecar position file
+ `O(skip N)` line cursor (and the cursor↔job-file desync failure mode) are
gone for the consumer who opts in. Rationale, the satellite-not-core
constraint, and the considered options:
[`docs/adr/0012-sqlite-embedded-store-satellite.md`](docs/adr/0012-sqlite-embedded-store-satellite.md).

This is **additive** — nothing is removed or changed in core or the existing
satellites. The core file scheduler and visited-link tracker are
byte-unchanged and remain the zero-dependency local default; SQLite is the
opt-in robust-local tier between them and the distributed Redis / Azure
Service Bus satellites.

- `ScraperEngineBuilder.WithSqliteScheduler(databasePath, dataCleanupOnStart?, logger?)`
  over the public `WithScheduler` seam; `SqliteScheduler : IScheduler`.
- `ScraperEngineBuilder.TrackVisitedLinksInSqlite(databasePath, dataCleanupOnStart?)`
  over the public `WithLinkTracker` seam; `SqliteVisitedLinkTracker :
  IVisitedLinkTracker`. The `visited(url PRIMARY KEY)` table *is* the set —
  no in-memory mirror (a deliberate ADR-0012 deviation from the file
  tracker, mirroring the Redis tracker).
- The `Job` payload uses the same `WebReaperJson` grammar as the core file
  scheduler and the Redis scheduler (ADR-0008) — full type fidelity.
- Satellite per ADR-0009 / ADR-0012: core does not reference
  `Microsoft.Data.Sqlite`, so the native `e_sqlite3` (SQLitePCLRaw) graph
  stays off the dependency-light, Native-AOT-zero-warning core. Like the
  other satellites it is deliberately not marked `IsAotCompatible`.
- Versioned `7.1.0`: a new satellite added after the `7.0.0` satellite wave;
  it depends on `WebReaper` core `7.0.0`.

ADR-0012 itself carried a one-line correction (landed with the first
implementation slice, called out loud): its Mechanism section had wrongly
said `FileScheduler` writes its position file *after* the yield — the whole
`IScheduler` family is claim-before-yield / at-most-once for the in-flight
job, and `SqliteScheduler` matches it. Decision and shape unaffected.

## 7.0.0 — Satellite adapter packages; dependency-light core (breaking)

Heavy third-party adapters move out of the core `WebReaper` package into
per-technology satellite packages, wired through the builder's public
registration seam. Rationale, design, and the deliberate clean-cut (no compat
shell): [`docs/adr/0009-registration-seam-and-satellite-adapters.md`](docs/adr/0009-registration-seam-and-satellite-adapters.md).

This release lands the full ADR-0009 satellite set: **Cosmos**, **Mongo**,
**Redis**, **Azure Service Bus** and **Puppeteer**. The core `WebReaper`
package no longer references any of those SDKs — a plain HTTP→file crawl
pulls none of them.

It also closes the ADR-0008-named JSONPath follow-up: `JsonSchemaBackend`'s
Newtonsoft `JToken` JSONPath cursor — the last Newtonsoft reach in core — is
migrated to an in-repo JSONPath-subset evaluator over
`System.Text.Json.Nodes.JsonNode`. The supported dialect is preserved exactly
(optional `$`/`$.` root, `.`-separated property paths, trailing `[*]` array
wildcard — the whole surface the `Schema` model drives, pinned by the JSON
test corpus). With `CookieStore` already on System.Text.Json, **core is now
entirely Newtonsoft-free**: the `Newtonsoft.Json` `PackageReference` is
dropped and the *whole* core (not just a scoped path) publishes Native-AOT
zero-warning — verified by `WebReaper.AotSmokeTest`, now extended to exercise
the JSON backend. Rationale and the doc-lag correction:
[`docs/adr/0008-system-text-json-typed-pipeline.md`](docs/adr/0008-system-text-json-typed-pipeline.md).

### Breaking changes

- **`WriteToCosmosDb` moved to the `WebReaper.Cosmos` package.** It is now an
  extension method over `ScraperEngineBuilder`'s public `AddSink` registration
  seam, not a core builder method. `SpiderBuilder.WriteToCosmosDb` is removed.
- **`CosmosSink` moved**: `WebReaper.Sinks.Concrete` → namespace and package
  `WebReaper.Cosmos`.
- **Core no longer references `Microsoft.Azure.Cosmos`.** A core-only consumer
  no longer pulls Cosmos (or its Newtonsoft + native ServiceInterop graph).
- **No compat forwarders.** A forwarder would still reference the package and
  defeat the dependency-light core (ADR-0009 SemVer).
- `WriteToCosmosDb` no longer auto-uses the builder's logger; it takes an
  optional `ILogger` argument (defaults to `NullLogger`).
- **`WriteToMongoDb`, `WithMongoDbConfigStorage`, `WithMongoDbCookieStorage`
  moved to the `WebReaper.Mongo` package.** They are now extension methods
  over `ScraperEngineBuilder`'s public `AddSink` / `WithConfigStorage` /
  `WithCookieStorage` registration seams. `SpiderBuilder.WithMongoDbConfigStorage`
  and `SpiderBuilder.WithMongoDbCookieStorage` are removed.
- **Mongo adapter types moved** to namespace and package `WebReaper.Mongo`:
  `MongoDbSink` (was `WebReaper.Sinks.Concrete`), `MongoDbScraperConfigStorage`
  (was `WebReaper.ConfigStorage.Concrete`), `MongoDbCookieStorage` (was
  `WebReaper.Core.CookieStorage.Concrete`), `MongoBlobStore` (was
  `WebReaper.DataAccess`).
- **Core no longer references `MongoDB.Driver`.** A core-only consumer no
  longer pulls it — and the transitive `SharpCompress` `GHSA-6c8g-7p36-r338`
  audit-suppression moves out of core with it (now in `WebReaper.Mongo`).
- The three Mongo builder extensions no longer auto-use the builder's logger;
  each takes an optional `ILogger` argument (defaults to `NullLogger`).
- **`WithRedisScheduler`, `TrackVisitedLinksInRedis`, `WriteToRedis`,
  `WithRedisConfigStorage`, `WithRedisCookieStorage` moved to the
  `WebReaper.Redis` package.** They are now extension methods over
  `ScraperEngineBuilder`'s public `WithScheduler` / `WithLinkTracker` /
  `AddSink` / `WithConfigStorage` / `WithCookieStorage` registration seams.
  `SpiderBuilder.WriteToRedis` / `WithRedisConfigStorage` /
  `WithRedisCookieStorage` are removed.
- **Redis adapter types moved** to namespace and package `WebReaper.Redis`:
  `RedisScheduler` (was `WebReaper.Core.Scheduler.Concrete`),
  `RedisVisitedLinkTracker` (was `WebReaper.Core.LinkTracker.Concrete`),
  `RedisSink` (was `WebReaper.Sinks.Concrete`), `RedisScraperConfigStorage`
  (was `WebReaper.ConfigStorage.Concrete`), `RedisCookieStorage` (was
  `WebReaper.Core.CookieStorage.Concrete`), `RedisBlobStore` and
  `RedisConnectionPool` (were `WebReaper.DataAccess`).
- **Core no longer references `StackExchange.Redis`.** A core-only consumer
  no longer pulls it. ADR-0005's one-`ConnectionMultiplexer`-per-connection-string
  invariant is preserved: `RedisConnectionPool` moves whole and stays the
  single resolver every Redis adapter in the package goes through.
- The Redis builder extensions that took a logger no longer auto-use the
  builder's; each takes an optional `ILogger` argument (defaults to
  `NullLogger`).
- **`WithAzureServiceBusScheduler` moved to the `WebReaper.AzureServiceBus`
  package.** It is now an extension method over `ScraperEngineBuilder`'s public
  `WithScheduler` registration seam, not a core builder method. There was no
  `SpiderBuilder` equivalent to remove.
- **`AzureServiceBusScheduler` moved** to namespace and package
  `WebReaper.AzureServiceBus` (was `WebReaper.Core.Scheduler.Concrete`).
- **Core no longer references `Azure.Messaging.ServiceBus`.** A core-only
  consumer no longer pulls it. `WithAzureServiceBusScheduler` took no logger,
  so its signature is unchanged.
- **The headless-browser page loader moved to the `WebReaper.Puppeteer`
  package; the core default page loader is now HTTP-only.**
  `BrowserPageLoadTransport` moved (`WebReaper.Core.Loaders.Concrete` →
  namespace and package `WebReaper.Puppeteer`); `CookieExtensions` /
  `ToPuppeteerCookies` moved (`WebReaper.Extensions` → namespace and package
  `WebReaper.Puppeteer`).
- **Core no longer references `PuppeteerSharp` / `PuppeteerExtraSharp`.** A
  core-only consumer no longer pulls them or the Chromium provisioning path,
  and the ADR-0008-named `BrowserPageLoadTransport` `Assembly.Location` IL3000
  trim/AOT finding leaves core with it (now in `WebReaper.Puppeteer`).
- **New `WithLoadTransport` registration seam on `ScraperEngineBuilder`.** It
  takes a factory —
  `Func<ICookiesStorage, IProxyProvider?, ILogger, IPageLoadTransport>` —
  invoked at build time with the builder's resolved cookie storage, optional
  proxy provider and logger (a deliberate refinement of ADR-0009's stated
  `WithLoadTransport(IPageLoadTransport)`; see that ADR's Deliberate
  consequences note). ADR-0004's one-`IPageLoader` /
  two-`IPageLoadTransport` dispatcher is unchanged — only the default
  composition of the Dynamic slot moved out of core.
- **`GetWithBrowser` / `FollowWithBrowser` / `PaginateWithBrowser` still
  compile, but Dynamic pages now require `WebReaper.Puppeteer`.** With no
  browser transport registered a Dynamic load throws an actionable
  `InvalidOperationException` (`BrowserNotConfiguredPageLoadTransport`)
  pointing at the package and `.WithPuppeteerPageLoader()`, instead of a
  silent default. `.WithPuppeteerPageLoader()` is parameterless and — because
  the seam is a factory — reproduces the pre-7.0 behaviour exactly: the one
  shared cookie container (issue #26) and the optional proxy applied the
  browser's own way.
- **`SpiderBuilder` is now `internal`; the bare-spider seam is
  `ScraperEngineBuilder.BuildSpider()`.** The ADR-0009 capstone: the public
  registration seam lives *only* on `ScraperEngineBuilder`, and
  `SpiderBuilder`'s duplicate public surface is gone. The distributed-worker
  pattern (crawl one queued `Job`, re-enqueue its children — see
  `Examples/WebReaper.AzureFuncs`) gets a bare `ISpider` from the new public
  `ScraperEngineBuilder.BuildSpider()` instead of `new SpiderBuilder()…Build()`.
  Unlike `BuildAsync()`, `BuildSpider()` does not build or persist a
  `ScraperConfig`, so it does not require `Get`/`Parse` — the worker's config
  is persisted separately and read from storage at crawl time.

### Why

- Dependency-light core: a plain HTTP→file crawl stops transitively pulling
  Cosmos + Newtonsoft + native interop, `MongoDB.Driver` + its transitive
  SharpCompress CVE, `StackExchange.Redis`, `Azure.Messaging.ServiceBus`, and
  `PuppeteerSharp` + the Chromium provisioning path (carrying the ADR-0008
  `BrowserPageLoadTransport` IL3000 finding out of core).
- The builder deepens into a small public registration seam; per-adapter
  `WriteToX` sugar ships with its adapter. See ADR-0009.

### Migration

- Add the package: `dotnet add package WebReaper.Cosmos`.
- Add `using WebReaper.Cosmos;` wherever you call `.WriteToCosmosDb(...)` or
  reference the `CosmosSink` type. `WriteToCosmosDb`'s existing arguments are
  unchanged (an optional `ILogger` is appended).
- Add the package: `dotnet add package WebReaper.Mongo`.
- Add `using WebReaper.Mongo;` wherever you call `.WriteToMongoDb(...)`,
  `.WithMongoDbConfigStorage(...)`, `.WithMongoDbCookieStorage(...)` or
  reference the `MongoDbSink` / `MongoDbScraperConfigStorage` /
  `MongoDbCookieStorage` / `MongoBlobStore` types. Existing arguments are
  unchanged (an optional `ILogger` is appended).
- Add the package: `dotnet add package WebReaper.Redis`.
- Add `using WebReaper.Redis;` wherever you call `.WithRedisScheduler(...)`,
  `.TrackVisitedLinksInRedis(...)`, `.WriteToRedis(...)`,
  `.WithRedisConfigStorage(...)`, `.WithRedisCookieStorage(...)` or reference
  the `RedisScheduler` / `RedisVisitedLinkTracker` / `RedisSink` /
  `RedisScraperConfigStorage` / `RedisCookieStorage` / `RedisBlobStore` /
  `RedisConnectionPool` types. Existing arguments are unchanged (an optional
  `ILogger` is appended where one was passed).
- Add the package: `dotnet add package WebReaper.AzureServiceBus`.
- Add `using WebReaper.AzureServiceBus;` wherever you call
  `.WithAzureServiceBusScheduler(...)` or reference the
  `AzureServiceBusScheduler` type. Its arguments are unchanged.
- Add the package: `dotnet add package WebReaper.Puppeteer`.
- Add `using WebReaper.Puppeteer;` and call `.WithPuppeteerPageLoader()` on
  the builder wherever you scrape Dynamic pages (`GetWithBrowser` /
  `FollowWithBrowser` / `PaginateWithBrowser`), or wherever you reference the
  `BrowserPageLoadTransport` type or `CookieContainer.ToPuppeteerCookies(...)`.
  `.WithPuppeteerPageLoader()` takes no arguments and reproduces the pre-7.0
  default behaviour. A core-only (HTTP) crawl needs no change.
- If you constructed `new SpiderBuilder()…Build()` directly (the
  distributed-worker pattern), switch to
  `new ScraperEngineBuilder()…BuildSpider()` — the same `WithLogger` /
  `WithLinkTracker` / `AddSink` / etc. configuration, returning the same
  `ISpider`. Fluent `ScraperEngineBuilder` consumers need no change.
- If your code used `Newtonsoft.Json` and relied on getting it *transitively*
  through the `WebReaper` package, add an explicit
  `<PackageReference Include="Newtonsoft.Json" .../>` — core no longer
  references it. WebReaper's own APIs are `System.Text.Json` throughout, so a
  consumer that does not use Newtonsoft itself needs no change.

No code or API change, listing only: each satellite package
(`WebReaper.Cosmos` / `.Mongo` / `.Redis` / `.AzureServiceBus` / `.Puppeteer`)
now ships a focused README, the shared WebReaper icon, and release notes in its
`.nupkg`, so its nuget.org page renders like the core package's instead of
blank.

No code or API change, IntelliSense only: the core package now ships its XML
documentation as `lib/<tfm>/WebReaper.xml` (it previously shipped an `API.xml`
the IDE never resolved next to `WebReaper.dll`, so consumers got no doc
tooltips); the `DocumentationFile` redirect that also wrote a build artifact
into the tracked tree is gone. Each satellite now generates and ships its own
XML doc too, with the builder-extension API (`WriteToCosmosDb`,
`WithRedisScheduler`, …) documented; the moved adapter classes remain
undocumented by design (CS1591 suppressed in the satellites only).

## 6.0.0 — System.Text.Json typed pipeline (breaking, AOT-clean)

The extraction and persistence pipeline moved off `Newtonsoft.Json` +
`TypeNameHandling.Auto` to `System.Text.Json` source-gen with a typed
`JsonObject` terminal. Rationale, design, and bounded scope:
[`docs/adr/0008-system-text-json-typed-pipeline.md`](docs/adr/0008-system-text-json-typed-pipeline.md)
(supersedes the serialization grammar of ADR-0002/0003; closes the ADR-0005
`RedisScheduler` `Job` round-trip).

### Breaking changes

- **`ParsedData.Data`** is now `System.Text.Json.Nodes.JsonObject` (was
  `Newtonsoft.Json.Linq.JObject`).
- **`IFileSinkFormat.Header(JsonObject)` / `FormatRow(JsonObject)`** (was
  `JObject`). Observable file content (CSV header/rows, JSON-lines) is
  unchanged.
- **`PostProcess(Func<Metadata, JsonObject, Task>)`** on `ScraperEngineBuilder`
  / `SpiderBuilder` (was `JObject`).
- **`IContentParser` removed.** The Newtonsoft `JObject`-returning
  `ParseAsync` is gone. Use `IJsonContentParser.ParseToJsonAsync` →
  `JsonObject`. The built-in parsers (`AngleSharpContentParser`,
  `JsonContentParser`, `XPathContentParser`, `SchemaContentParser<TNode>`)
  implement `IJsonContentParser`; `WithContentParser` now takes
  `IJsonContentParser`.
- **Persisted/wire format changed.** Config, every `Job` (Redis, Azure Service
  Bus, File schedulers), and cookies now serialize via System.Text.Json
  source-gen (no `TypeNameHandling`). Polymorphic `PageAction.Parameters`,
  the `ImmutableQueue<LinkPathSelector>` chain, and `Schema`/`SchemaElement`
  round-trip via dedicated converters. **Clear distributed job queues and
  stored scraper config on upgrade** — old Newtonsoft-format payloads are not
  read by the new grammar.

### Why

- Removes Newtonsoft's reflection / `TypeNameHandling` — a *documented* bug
  class (the ADR-0003 file-adapter serialize-`Auto`/deserialize-defaults
  asymmetry; the ADR-0005 `RedisScheduler` `Job` asymmetry), now closed
  uniformly across all schedulers.
- AOT-clean typed pipeline: the Newtonsoft-free configuration
  (markup/CSS/XPath + STJ config/schedulers/sinks) publishes Native-AOT with
  zero trim/AOT warnings (verified by a CI `WebReaper.AotSmokeTest`); the
  library declares `IsAotCompatible`.
- Smaller, single-file, no-runtime-install deploy footprint.

### Migration

- Replace `JObject`/`JToken` in your `PostProcess`, custom sink, or custom
  parser code with `System.Text.Json.Nodes`: `obj["k"]!.GetValue<T>()`,
  `obj["k"]!.ToString()`, `obj["k"]!.AsArray()`, `JsonNode.DeepEquals(...)`.
- `parser.ParseAsync(...)` → `parser.ParseToJsonAsync(...)`.
- Drain Redis/Azure Service Bus job queues and delete stored config produced
  by ≤ 5.1.0 before running 6.0.0.

### Not removed (still Newtonsoft, opt-in, AOT-dirty only if used)

- **JSON-endpoint scraping** (`JsonContentParser` / `JsonSchemaBackend`): the
  JSONPath scope cursor is Newtonsoft `JToken` — System.Text.Json has no
  JSONPath. Named ADR-0008 follow-up.
- **`CosmosSink`**: the Cosmos SDK is itself Newtonsoft-coupled. See ADR-0008
  Bounded scope.

A consumer that uses neither still gets a fully AOT-clean publish (unreached
Newtonsoft is trimmed away).

## 5.1.0

- XPath selector backend (`AngleSharpXPathSchemaBackend`), discussion #17,
  ADR-0007.

## 5.0.0

- One page-loader seam with internal transports (ADR-0004); one keyed blob
  store + payload shells (ADR-0003); `RedisConnectionPool` (ADR-0005);
  buffered file-sink drain (ADR-0006).
