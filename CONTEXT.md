# WebReaper

The domain language of WebReaper — a declarative, parallel web crawler. This file names the concepts a contributor must share to reason about the crawl correctly; it is the reference the architecture review defers to.

## Language

### Crawl topology

**Crawl**:
One end-to-end run of the scraper over a site, seeded from the configured start URLs.
_Avoid_: scrape, scan, session.

**Crawl seed**:
The start URL(s) and page mode you hold *before* a builder exists — produced by the static `ScraperEngineBuilder.Crawl(...)` / `CrawlWithBrowser(...)`, awaiting a Schema. Its only operation is `Extract(schema)`, which yields the configurable builder; the build terminals live only there, so "build with no start URLs or no schema" is unrepresentable rather than a runtime throw (see `docs/adr/0025-staged-builder-entry.md`).
_Avoid_: builder, config, prerequisites, start set.

**Job**:
A single URL to visit, plus the remaining selector chain and its parent backlinks; the unit the scheduler queues and a worker consumes.
_Avoid_: task, work item, request.

**Selector chain**:
The ordered queue of `LinkPathSelector`s a Job has left to apply before it reaches a target page; its length and its head's pagination flag *are* the crawl state.
_Avoid_: selector list, path, route.

**Page category**:
Which of three roles a Job's page plays — **derived** from the selector chain, never stored on the Job. It is realized as the arm of the **Crawl outcome** the **Crawl step** returns, not as a property of the input (the old `Job.PageCategory` was removed).
_Avoid_: page type (that is the Static/Dynamic load mode — see below), state, kind.

- **Target page**: a Job whose selector chain is empty; its page is parsed with the Schema, yields one ParsedData, and produces no further Jobs.
- **Transit page**: a Job with selectors remaining and no pagination on the head selector; its page yields child Jobs by following the head selector.
- **Page with pagination**: a Job with exactly one selector left that carries a pagination selector; its page yields both item Jobs and next-page Jobs.

### The crawl step

**Crawl step**:
The pure decision that maps a Job, its loaded document, and the config to exactly one crawl outcome. The Spider is the I/O shell around the crawl step, not the crawl step itself.
_Avoid_: spider, handler, processor.

**Crawl outcome**:
The result of a crawl step — a closed sum with exactly three arms: a target page's ParsedData, a transit page's followed Jobs, or a paginated page's item + next-page Jobs. Never more than one arm; not extensible (see `docs/adr/0001-crawl-outcome-closed-sum.md`).
_Avoid_: result, response.

**Advance**:
What a transit page does to the selector chain — consume (dequeue) the head selector, so child Jobs carry the shortened chain.
_Avoid_: descend, pop, next.

**Retain**:
What a page with pagination does — leave the selector chain unchanged, so next-page Jobs re-apply the same step (page 2 of a listing is the same step, not a deeper one).
_Avoid_: repeat, stay, hold.

### The Crawl driver

**Crawl driver**:
The role that drives a Crawl: seed a Job per start URL, schedule Jobs, own the Crawl-global state (the Visited-link tracker and the stop rule), interpret each Job report, and detect termination. Two adapters — the in-process driver (`ScraperEngine`, `Parallel.ForEachAsync`) and the distributed driver (one queue message per Job). It holds one Spider and is the only thing that fans out to Sinks and fires the PostProcessor / ScrapedData notification (those moved off the Spider — see `docs/adr/0022-crawl-driver-and-outstanding-work-latch.md`).
_Avoid_: engine, runner, orchestrator, coordinator, master.

**Spider**:
The per-Job I/O shell: load one Job's page, run the Crawl step, return a Job report — nothing else. It does NOT own the Visited-link tracker, the crawl-limit stop, Sink fan-out, or the callbacks; those are the Crawl driver's. The ADR 0001 posture one layer up: the shell *reports* what happened, the driver *decides* what to do.
_Avoid_: crawler, worker, handler, the spider that fans out.

**Distributed spider builder**:
The ADR-0009 reduced shell — `DistributedSpiderBuilder`, the seedless public seam a distributed worker uses to build its Spider. Config-agnostic by construction: no Crawl seed, no `BuildAsync`, no crawl-shape — the worker's `ScraperConfig` is authored and persisted by the start endpoint and read from shared storage at crawl time. A separate type from the engine builder on purpose ("two seams, not one bug"); shared adapters are wired by hand via public satellite concretes / DI (see `docs/adr/0025-staged-builder-entry.md`).
_Avoid_: spider builder (unqualified), worker, the engine builder, ScraperEngineBuilder.

**Job report**:
The closed value `Spider.CrawlAsync` returns: the optional ParsedData to emit (present iff a Target page), the discovered child Jobs (unfiltered — dedup is the driver's), and the accounting facts the Outstanding-work latch needs (this Job's identity, its child count). It *wraps* the Crawl step's Crawl outcome with emission and accounting facts; it does not replace or extend the closed `CrawlOutcome` sum (ADR 0001 stands). Termination is a field here, never a thrown exception.
_Avoid_: result, outcome (that is the Crawl step's), response, crawl result.

**Visited-link tracker**:
Crawl-global state — what *the Crawl* has visited — owned by the Crawl driver, not the Spider. Its atomic test-and-set ("was this URL newly added?") is the single idempotency authority that gates three things at once: discovery dedup, the page-limit gate, and Outstanding-work-latch accounting. One atomic membership check, three uses; this is what makes the latch correct independent of at-least-once queue delivery.
_Avoid_: seen set, cache, dedup filter, link tracker (unqualified).

**Outstanding-work latch**:
The Crawl driver's termination detector: a centralized unit-credit counter (seed = #start URLs; per Job, register children and add their credit *before* decrementing the parent; reaching zero trips the latch exactly once, firing the idempotent end-of-crawl action via a compare-and-set). The credit-conservation ordering — account for children before the parent is marked done — is the correctness precondition. One seam, two adapters: in-memory `Interlocked` (in-process driver) and a distributed-atomic counter (Redis/Cosmos, reusing ADR 0003/0005 infra). The soft page limit and `StopWhenDrained` are the same latch, not separate mechanisms. See `docs/adr/0022-crawl-driver-and-outstanding-work-latch.md`.
_Avoid_: pending counter, drain flag, semaphore, barrier.

**Retry policy**:
The wrapper around each per-Job Spider call (ADR-0026) that bounds retries on transient failures before the Crawl driver gives up on the Job. One seam (`IRetryPolicy.ExecuteAsync<T>`), one core default (`FixedAttemptsRetryPolicy` — four attempts total, no delay, every exception except `OperationCanceledException` triggers a retry). Custom policies (a Polly resilience pipeline with exponential backoff, a no-retry adapter for deterministic tests, a future satellite-aware policy) wire in through the **Registration seam** via `WithRetryPolicy`. Cancellation is cooperative by contract: an implementation that retries `OperationCanceledException` violates the seam. Lives only on the in-process Crawl driver; the distributed-worker reduced shell does not use one (its retry knob is the queue's redelivery / visibility-timeout).
_Avoid_: resilience policy, retry strategy, retrier.

### Extraction & output

**Schema**:
The declarative field→selector tree describing what to extract from a target page.
_Avoid_: template, mapping, model.

**Schema fold**:
The single recursive interpretation of a Schema against a loaded document — container vs object-list vs leaf vs value-list, type coercion, the missing-node policy, the swallow-and-log scope. It has exactly one home (`SchemaContentParser<TNode>`); a backend never re-implements it.
_Avoid_: walk, traversal, visitor, parser (the parser is the shell around the fold).

**Node backend**:
The per-document-shape seam the fold calls (`ISchemaBackend<TNode>`): parse a root, select-many, select-one, extract a leaf's **raw value**. HTML/CSS, JSON/JSONPath, and HTML/XPath are three backends; the seam is the only place document-specific quirks live.
_Avoid_: parser, loader, driver.

**Raw value**:
What a backend's `ExtractRaw` returns for a leaf *before* coercion — a `string` for text/markup backends, a native token for structured ones. An *untyped* leaf is this value verbatim; that single fact (not duplicated code) is the entire HTML-vs-JSON output difference.
_Avoid_: text, content, the value.

**Typed coercion**:
The Schema→CLR conversion the fold applies when `SchemaElement.Type` is set (`Integer`→`int.Parse`, …). Shared grammar, backend-independent — never a per-backend concern.
_Avoid_: parsing, casting, conversion (unqualified).

**ParsedData**:
The extracted record from one target page — its URL plus a JSON object.
_Avoid_: result, item, row.

**Sink**:
A destination a ParsedData is emitted to (file, Redis, Cosmos, …).
_Avoid_: output, writer, exporter.

**File sink drain**:
The one home for the buffered file-write mechanism (`BufferedFileSink`): a
producer enqueues rows, one background consumer appends them to the file;
cleanup and directory creation happen once, eagerly, at construction. Every
file **Sink** is this drain plus a **Row format**; the drain never knows JSON
from CSV.
_Avoid_: writer, buffer, file sink (unqualified).

**Row format**:
The single quarantined per-format quirk a **File sink drain** delegates to
(`IFileSinkFormat`): how one row becomes a line, and whether the file carries a
header derived from the first row. JSON-lines and CSV are two **Row format**s —
the only legitimate difference between file sinks.
_Avoid_: serializer, formatter, encoder.

### Crawl-state persistence

**Keyed blob store**:
A backend-agnostic seam that persists and fetches one opaque UTF-8 string under a key, last-write-wins, with `null` meaning absent; its adapters are the only place the *keyed* persistence mechanism lives. The file adapter keeps its own whole-file replace/read but delegates directory creation, cleanup-on-start, and the missing-file policy to **File persistence prep**.
_Avoid_: repository, cache, document DB, config storage.

**Payload shell**:
The thin module above a **keyed blob store** that owns one payload's serialization grammar and quirks (the config shell owns its `System.Text.Json` source-gen grammar — ADR-0008, replacing `TypeNameHandling.Auto`; the cookie shell owns `CookieContainer ↔ CookieCollection`) and decides what *absent* means for that payload.
_Avoid_: storage, provider, serializer.

**File persistence prep**:
The one home for the three things every file-backed adapter must get right
*before* it writes: create the file's directory eagerly and unconditionally
(`Directory.CreateDirectory`, idempotent), apply `DataCleanupOnStart`
deterministically at start (not lazily on first write), and read a missing
file as empty rather than throwing. A small *stateless* helper — **not** a
held-handle durability layer. Durable append stays the adapter's own
per-call write or, where one already exists, a single-writer drain
(`BufferedFileSink`): concurrent durable append is a single-writer concern
(the **File sink drain**'s one-consumer pattern), deliberately not a shared
lock. A file **Keyed blob store** (whole-file replace), a file **Scheduler**
(its append + resumable cursor + position file), a file **Visited-link
tracker** (its append + in-memory mirror), and a **File sink drain** (its
buffered drain) each keep their own write/read essence and only delegate
this prep. The held-handle/lock *substrate* was considered and rejected as
over-built and un-idiomatic; `FileScheduler`'s file-as-queue → embedded
store is **decided** — adopted as the `WebReaper.Sqlite` satellite
(ADR-0012), *not* a core replacement: the core `FileScheduler` stays the
zero-dep default and SQLite is an opt-in robust-local tier (see
`docs/adr/0012-sqlite-embedded-store-satellite.md`).
_Avoid_: durable file / file substrate (the held-handle substrate was rejected — see Flagged ambiguities), file store, journal.

### Page loading

**Page loader**:
The single seam (`IPageLoader`) that turns a **PageRequest** into a page's HTML, dispatching on the request's **PageType** to one **load transport**; the Spider holds one of these and is loader-blind (no Static/Dynamic loader pair).
_Avoid_: page fetcher, downloader, static/dynamic loader.

**Load transport**:
The per-mechanism adapter behind the **page loader** — HTTP (`HttpPageLoadTransport`) or headless browser (`BrowserPageLoadTransport`) — and the only place that mechanism's client/launch quirks and its proxy application live.
_Avoid_: requester, loader, driver, channel.

**PageRequest**:
What the **page loader** needs to fetch one page — URL, **PageType**, optional page actions, headless flag — projected from a **Job** plus the crawl's headless setting (the loader never sees the selector chain or backlinks).
_Avoid_: load args, request.

### Wiring

**Core adapter**:
An adapter that ships in the `WebReaper` package and pulls no third-party SDK: the in-memory / file / console / CSV / JSON-lines **Sink**s and stores, the HTTP **Load transport**, the CSS and XPath **Node backend**s. The dependency-light surface a consumer gets with zero extra packages.
_Avoid_: built-in, default adapter, in-box.

**Satellite adapter**:
An adapter whose SDK is heavy or AOT-hostile, shipped in its own package (`WebReaper.Cosmos`, `WebReaper.Mongo`, `WebReaper.Redis`, `WebReaper.AzureServiceBus`, `WebReaper.Puppeteer`) and attached through the **Registration seam** by an extension-method builder that lives in that package. Core never references it; the dependency enters the consumer's graph only when they reference the satellite.
_Avoid_: plugin, optional dependency, module, add-on.

**Registration seam**:
The builder's small set of public methods over the existing role interfaces (`AddSink(IScraperSink)`, `AddScheduler(IScheduler)`, the config / cookie / visited-link / **Load transport** equivalents) through which every adapter is wired — **Core adapter**, **Satellite adapter**, or a consumer's own. The builder is a registry over role interfaces, not a factory of concrete adapters; the `WriteToX(connectionString)` convenience is sugar over this seam and lives with its **Satellite adapter**, never in core.
_Avoid_: factory, DI container, plugin host, service registration.

## Relationships

- A **Crawl** processes many **Job**s.
- A **Job** carries a **Selector chain**; the chain's length and head derive the **Page category** (it is not stored on the Job).
- A **Crawl step** maps one **Job** to one **Crawl outcome**.
- A **Target page** outcome produces one **ParsedData**, emitted to every **Sink**.
- A file **Sink** is one **File sink drain** plus one **Row format**; the drain is shared, the format is the only per-format variation.
- A **Transit page** **advance**s the selector chain; a **Page with pagination** **retain**s it.
- A **Schema fold** interprets a **Schema** by calling one **Node backend**; the backend yields **raw value**s, the fold applies **typed coercion**.
- A **Payload shell** serializes one payload and delegates storage to one **Keyed blob store**; the store never knows which payload it holds.
- A file **Keyed blob store**, file **Scheduler**, file **Visited-link tracker**, and **File sink drain** each keep their own write/read essence and share only **File persistence prep** (eager directory creation, deterministic cleanup-on-start, missing-file-as-empty). Concurrent durable append is a single-writer concern (the **File sink drain**'s one-consumer pattern), deliberately not a shared lock; the **Scheduler**'s resumable cursor + position file stay role-local.
- The **Spider** calls one **Page loader** with a **PageRequest**; the loader dispatches on **PageType** to one **Load transport**, which applies the optional proxy its own way.
- The builder wires every adapter — **Core adapter**, **Satellite adapter**, or consumer-supplied — through the **Registration seam** over a role interface; it never constructs a **Satellite adapter** itself.
- A **Crawl driver** drives a **Crawl**: it seeds a **Job** per start URL, schedules **Job**s, owns the **Visited-link tracker** and the stop rule, and interprets each **Job report**.
- The **Crawl driver** holds one **Spider**; per **Job** it calls the **Spider**, receives a **Job report**, applies the **Visited-link tracker** test-and-set (the idempotency authority), enqueues surviving child **Job**s, mutates the **Outstanding-work latch**, fans out **ParsedData** to every **Sink**, and fires the PostProcessor / ScrapedData notification.
- The **Spider** loads the page, runs the **Crawl step**, and returns a **Job report**; it no longer touches the **Visited-link tracker**, the limit, or the **Sink**s.
- The **Outstanding-work latch** trips once when credit reaches zero; its correctness rests on the **Visited-link tracker**'s atomic test-and-set, not on queue delivery semantics. The in-process and distributed **Crawl driver**s are its two adapters.
- The in-process **Crawl driver** holds one **Retry policy** and runs every per-Job **Spider** call through `IRetryPolicy.ExecuteAsync`; the policy bounds attempts and never retries cancellation. The distributed-worker reduced shell holds no **Retry policy** — its retry boundary is the queue's redelivery.

## Example dialogue

> **Dev:** "When a page-with-pagination crawl runs, do the item Jobs and the next-page Jobs both keep the same selector chain?"
> **Domain expert:** "No. The item Jobs **advance** — the listing selector is consumed, so they're target pages now. The next-page Jobs **retain** the chain, because page 2 of the listing is the same step, not a deeper one."

> **Dev:** "The Mongo config store used to keep a queryable BSON document; now it's a string blob — isn't that a regression?"
> **Domain expert:** "No. WebReaper only ever fetches a whole config by key, never queries inside it. The **keyed blob store** holds an opaque string; the `System.Text.Json` source-gen converters that round-trip `PageAction.Parameters` (`object[]`) and the `ImmutableQueue` selector chain live in the config **payload shell**, not the store (ADR-0008's `$kind`/kind-tagged grammar, formerly `TypeNameHandling.Auto`). The BSON shape was never load-bearing."

> **Dev:** "How does the Spider decide between an HTTP fetch and a headless browser?"
> **Domain expert:** "It doesn't — it builds a **PageRequest** and hands it to the one **page loader**. The loader reads **PageType** and dispatches to the HTTP or browser **load transport**. Whether a proxy is used, and how it's applied, is the transport's business, not the Spider's."

## Flagged ambiguities

- **Selector-chain handling of pagination vs following was implicit.** In `Spider.CrawlAsync` one call site passed the dequeued chain and another the original chain, with nothing naming the difference. Resolved structurally: the **Crawl step** returns a **Crawl outcome** whose `Followed.Next` and `Paginated.Items` carry the **advance**d chain and whose `Paginated.NextPages` carries the **retain**ed chain — the two rules are now distinct named fields, not two look-alike call sites (see `docs/adr/0001-crawl-outcome-closed-sum.md`).
- **"Page type" vs "page category".** Resolved: **page category** = Target / Transit / Pagination, derived from the selector chain. **PageType** is the load mode (Static vs Dynamic, i.e. HTTP vs Puppeteer). Distinct concepts — never conflate them.
- **The HTML-vs-JSON untyped-leaf difference was accidental duplication; it is now a deliberate, pinned property.** An untyped leaf is the **raw value** verbatim: HTML yields a string, JSON keeps its native number/bool. This is intentional (JSON-endpoint users depend on native types) and is the *only* legitimate behavioural difference between backends — it rides on `ExtractRaw`'s return type, not copied code, and is pinned cross-backend in `SchemaFoldTests`. Do not "unify" it (see `docs/adr/0002-schema-fold-and-node-backend-seam.md`).
- **Previously-divergent log/selector behaviour is now uniform — by design, not regression.** The missing-node and parsing-error log messages were textually different per backend, and the HTML single-value path tolerated a missing selector where the list paths did not. The fold makes all three uniform. Observable outcomes (field left empty/unset, parse not aborted) are unchanged; only the divergent log text and the single-value selector-miss mechanism were unified.
- **The config/cookie persistence stores were eight near-duplicate classes; the duplication had drifted into real bugs. Now one keyed blob store + per-payload shells — deliberate, not regression.** Mongo stores an opaque `{id, blob}`, not a queried BSON projection (never queried — do not "restore" it); the missing-value policy is uniform (`null` ⇔ absent at the store; the config shell throws a typed not-found, the cookie shell returns an empty `CookieContainer`), replacing the File adapter's `NullReferenceException` and the silent-null divergence; `PutAsync` is upsert-by-key, fixing the Mongo append/read-oldest bug; `ScraperConfig` round-trips with full type fidelity through *every* backend (Redis was silently lossy; ADR-0003 used `TypeNameHandling.Auto` here, itself later retired by ADR-0008 for the `System.Text.Json` source-gen grammar); in-memory storage now round-trips through the shell's serializer like every other backend (was: held the live object), so the cheap path exercises the same serialization. `RedisBase`'s process-static single-multiplexer bug is fully resolved: `RedisBase` is retired and all four Redis adapters (blob store, scheduler, sink, visited-link tracker) share one `RedisConnectionPool` — one multiplexer per connection string, no statics. See `docs/adr/0003-keyed-blob-store-and-payload-shells.md` and `docs/adr/0005-redis-connection-pool.md`.
- **The Static/Dynamic loader split was two single-adapter seams plus a copy-pasted requester triad and Puppeteer pair; the proxy/no-proxy choice had no home. Now one `IPageLoader` + two `IPageLoadTransport`s — breaking, deliberate.** `IStaticPageLoader` had exactly one implementation; the proxy decision was re-made in the builder branch, the requester triad, and the Puppeteer pair, with bugs drifted into the copies. The Spider no longer dispatches by load mode (that home moved behind the **page loader**); `IStaticPageLoader`, `IBrowserPageLoader`, `IPageRequester` + its three impls, and the two Puppeteer classes are removed (major SemVer). Fixed by construction, deliberately: the non-proxy static path now actually applies stored cookies (the handler was previously built *before* the cookie container was set); one canonical User-Agent (the triad had two by copy-drift); one canonical browser navigation wait, `Networkidle2` (the Puppeteer pair had `DOMContentLoaded` vs `Networkidle2` by accidental drift); the never-constructed, buggy `ProxyPageRequester` is gone. Out of scope, preserved as-is: the browser page-action table still handles only four of six `PageActionType`s (a missing feature, not this duplication deepening). See `docs/adr/0004-one-page-loader-transport-seam.md`.
- **The distributed scheduler's `Job` round-trip is serialize/deserialize-asymmetric — named, not yet fixed.** `RedisScheduler` writes a `Job` with `TypeNameHandling.None` and reads it back with default settings, so a `Job`'s `ImmutableQueue<LinkPathSelector>` and `PageAction.Parameters` (`object[]`) lose the type metadata they need to rematerialise — the same asymmetry ADR 0003 fixed for the config payload. The `RedisBase` retirement (ADR 0005) preserved this verbatim rather than widen its scope; it is a distinct future candidate, not a regression introduced there. **Update:** that candidate is decided — ADR 0008 retires `TypeNameHandling` entirely for a `System.Text.Json` source-gen + converters grammar, and `RedisScheduler` serialises/deserialises a `Job` through the *same* context as the config payload, so the asymmetry becomes unrepresentable (there is no `TypeNameHandling` knob to set differently on the two sides). Decided and Phase-0-spike-proven; landed staged, not big-bang. See `docs/adr/0005-redis-connection-pool.md` and `docs/adr/0008-system-text-json-typed-pipeline.md`.
- **The two file sinks were one buffered drain copy-pasted with drifted bugs; now one `BufferedFileSink` + an `IFileSinkFormat` quirk — deliberate, not regression.** Cleanup and directory creation are now eager and unconditional (deterministic even for a zero-row crawl; old CSV kept stale data and never created the directory, old JSON-lines created it only when cleaning); one consumer is started once, bound to the first emit's token (old JSON-lines used `CancellationToken.None` with dead re-init code, old CSV could double-spawn it under concurrent first emits). Observable file content is unchanged. Out of scope, preserved verbatim: one `File.AppendAllTextAsync` per row and no consumer flush/dispose — a *shared* property of the old code, a separate future candidate. See `docs/adr/0006-file-sink-buffered-drain.md`.
- **XPath is a third `ISchemaBackend`, and it deliberately does not copy the CSS backend's `src`→`title` rewrite — by design, not inconsistency.** Discussion #17 asked for XPath/RegEx; XPath shipped as `AngleSharpXPathSchemaBackend` over the same AngleSharp DOM (the ADR 0002 seam used as intended, fold unduplicated). The CSS backend's requested-`src`→`title` rewrite is a quarantined legacy quirk of *that* backend; per ADR 0002 quirks are backend-local, so the XPath backend returns the attribute asked for (the only behavioural difference, pinned by a test). RegEx selectors were declined: a regex over markup has no node scope and cannot satisfy the `SelectMany`/`SelectOne` contract. Link discovery stays CSS (out of scope, as with the JSON backend). See `docs/adr/0007-xpath-schema-backend.md`. **Update:** the *attribute-vs-text-vs-html dispatch* that both AngleSharp backends shared verbatim (the markup-leaf grammar, distinct from the `src`→`title` quirk) is now one home — `AngleSharpRawExtractor` (Tier-2 internal static) — and each AngleSharp backend's `ExtractRaw` shrinks to "apply this backend's quirks, then delegate." The seam `ISchemaBackend<TNode>` is unchanged; the JSON backend is unchanged (its grammar is a `JsonNode.DeepClone()`, structurally different from markup). ADR-0007's pinned behavioural difference still stands — the CSS backend applies the rewrite before delegation, the XPath backend does not — but now structurally, not by code duplication. See `docs/adr/0027-anglesharp-raw-extractor.md`.
- **The AngleSharp-DOM markup-leaf grammar lived as a 7-line `if/else if/else` dispatch copy-pasted across both AngleSharp backends; now one internal static helper. Internal-only refactor, deliberate.** `AngleSharpSchemaBackend.ExtractRaw` and `AngleSharpXPathSchemaBackend.ExtractRaw` shared the same three-arm dispatch (attribute / inner-HTML / text against an `IElement`) with the CSS `src`→`title` rewrite as the only legitimate difference — a misapplication of ADR-0002's "quirks are backend-local," because the dispatch itself is the **Raw value** grammar (driven by `SchemaElement.Attr` / `SchemaElement.GetHtml`), not a quirk. A third AngleSharp-DOM backend (Fizzler, …) would re-derive the dispatch and drift. Resolved: `AngleSharpRawExtractor.ExtractRaw(IElement, SchemaElement)` is the one home; the CSS backend's `ExtractRaw` is the `src`→`title` mutation + a one-line delegation; the XPath backend's is a one-line expression-bodied delegation. JSON backend untouched (its grammar is structurally different — a `JsonNode.DeepClone()` preserving native value-kind, the ADR-0002 untyped-leaf raw-value pin). `ISchemaBackend<TNode>` seam unchanged; consumer / satellite backends see no public-surface change. Rejected with load-bearing reasons so future reviews don't re-suggest them: pushing the dispatch into the fold (forces the JSON backend to implement three meaningless methods and breaks the ADR-0007 quirk quarantine); an abstract base class the two AngleSharp backends extend (the backends already diverge on `SelectMany`/`SelectOne` shape — the base would carry nothing else); two copies pinned by test (catches drift after the fact, not by construction); generalising the helper to a third method on `ISchemaBackend<TNode>` (broad-seam-over-narrow-pattern; the JSON backend would not use it). See `docs/adr/0027-anglesharp-raw-extractor.md`.
- **Every third-party-SDK adapter was a hard core `PackageReference` wired by the builder's concrete `WriteToX` methods; now the builder is a public registration seam and the heavy adapters are per-technology satellite packages — breaking, deliberate.** `ScraperEngineBuilder` statically `new`d ~11 concrete adapters, forcing `Microsoft.Azure.Cosmos` (→ Newtonsoft + native `ServiceInterop`), `MongoDB.Driver` (→ the suppressed `SharpCompress` CVE), `StackExchange.Redis`, `Azure.Messaging.ServiceBus`, and `PuppeteerSharp` (→ Chromium) into every consumer's graph, including a plain HTTP→file crawl. The seam interfaces were already deep (ADR 0003/0004/0008) and five of six builder registration methods already existed; the deepening is mostly deletion plus one `WithLoadTransport`. `CosmosSink`/`MongoDbSink`/the Redis family/`AzureServiceBusScheduler`/`BrowserPageLoadTransport` move to `WebReaper.Cosmos`/`.Mongo`/`.Redis`/`.AzureServiceBus`/`.Puppeteer` and re-attach as extension methods over the public **Registration seam**; `SpiderBuilder`'s duplicate public adapter surface is removed and it is made `internal`; the default **Page loader** is HTTP-only (`GetWithBrowser` is opt-in via `WebReaper.Puppeteer`). Clean cut, no compat forwarder — a forwarder still `new`s the adapter so core would still reference the package, defeating the dependency-light core; a deliberate departure from the ADR 0002/0003/0004/0008 staged-compat precedent. Newtonsoft still does not leave core (the JSON-backend JSONPath cursor and `CookieStore`, ADR 0008 follow-ups, are untouched); zero-warning core `PublishAot` remains gated on ADR 0008's named JSONPath migration. See `docs/adr/0009-registration-seam-and-satellite-adapters.md`. **Update:** that candidate is decided and shipped (2026-05-17). The JSON-backend JSONPath cursor was migrated to an **in-repo JSONPath-subset evaluator over `System.Text.Json.Nodes.JsonNode`** — deliberately *not* a JSONPath dependency (a new core dep would contradict this bullet's own dependency-light result; the only dialect `Schema` drives — optional `$`/`$.` root, `.`-separated property segments, a trailing `[*]` array wildcard — is small and fully pinned by the JSON test corpus, and an RFC-9535 library would reject WebReaper's relative non-`$` selectors anyway). `CookieStore` needed no migration: it has been on the `WebReaperJson` System.Text.Json source-gen over a flat `CookieDto` since 6.0.0 — the "untouched" / ADR-0008 "preserved verbatim" prose was itself stale (the same docs-follow-code lag class as ADR-0008's own post-release corrections). Core therefore has zero Newtonsoft code reach: the `Newtonsoft.Json` `PackageReference` is dropped from `WebReaper.csproj` and the *whole* core (not the scoped Newtonsoft-free path ADR 0008 could originally promise) publishes Native-AOT zero-warning, proven by `WebReaper.AotSmokeTest` extended to drive the JSON backend (RED `IL2104`/`IL3053` Newtonsoft rollup before, green after). The only Newtonsoft left in the graph is `WebReaper.Cosmos`'s `CosmosSink` via the Cosmos SDK — the satellite, off the core graph by this bullet's own decision, deliberately not `IsAotCompatible`. See `docs/adr/0008-system-text-json-typed-pipeline.md` ("JSONPath follow-up closed (2026-05-17)").
- **Four file-backed adapters hand-rolled the same pre-write file handling and drifted into three single-copy bugs; the fix is one small stateless prep helper — not a held-handle substrate.** `FileBlobStore` created no directory (threw on any nested path, contradicting its own "the key *is* the file path" doc), `FileVisitedLinkedTracker` created it only when the file was absent (threw if the file existed but its directory had since been removed), only `FileScheduler`/`BufferedFileSink` did it eagerly and unconditionally; `DataCleanupOnStart` timing diverged the same way — the ADR-0002/0003/0006 "copies drifted into bugs that exist in only one copy" shape, in the file-backed cluster ADR 0003 scoped out ("a set store, a queue, an append sink"). The genuinely shared, bug-prone part — eager unconditional directory creation, deterministic cleanup-on-start, missing-file-as-empty — moves to one stateless **File persistence prep** helper. An earlier draft proposed a stateful *Durable file substrate* (one held write handle per path, write-through flush, a shared single-writer lock) that additionally **superseded ADR 0006's deferred open/close-churn fence**; rejected on .NET-idiom grounds — idiomatic concurrent durable append in .NET is a single-writer funnel (a `Channel`/`BlockingCollection` + one consumer, exactly `BufferedFileSink`'s existing drain), not a shared `SemaphoreSlim` per write, so a lock-substrate would standardize the *weaker* pattern and force the one idiomatic adapter to opt out; and the held handle re-opened the lifetime scope ADR 0005/0006 bounded out. **ADR 0006's fence stands** (per-row open/close is still its own deferred candidate, not closed here); the earlier draft's "`FileBlobStore` replace becomes serialised" consequence is **withdrawn** — no shared lock is introduced, so no behaviour changes. Separately named, distinct future candidate (not actioned): `FileScheduler`'s file-as-queue — an append-only job file polled with a 300 ms delay plus a sidecar position file — reimplements a durable queue/WAL; the idiomatic .NET answer for resumable local durable state is an embedded store (SQLite via `Microsoft.Data.Sqlite`), which deletes the poll loop and position file, the distributed case already covered by the satellited Redis / Azure Service Bus schedulers. See `docs/adr/0011-file-persistence-prep.md`. **Update:** that candidate is decided — adopted as the new **`WebReaper.Sqlite` satellite** (scheduler + visited-link tracker), *not* a core replacement: `Microsoft.Data.Sqlite` is a native-interop dependency (native `e_sqlite3` via SQLitePCLRaw), the exact class ADR-0009 quarantines off core, so the core `FileScheduler` poll loop + position file *stay* as the zero-dep default and SQLite is an opt-in robust-local tier ("the poll loop disappears" holds only for the opt-in consumer; the producer/consumer empty-wait remains, as `RedisScheduler` also polls). `SqliteVisitedLinkTracker` deliberately keeps no in-memory mirror — the table *is* the set, mirroring `RedisVisitedLinkTracker`, not the file adapter. The config blob stays on `FileBlobStore` (write-once/read-once, ADR-0003, no poll loop); a Sqlite config/blob store and a Sqlite sink are named, not-actioned future candidates. Purely additive SemVer (new satellite, no core change). See `docs/adr/0012-sqlite-embedded-store-satellite.md`.
- **The Crawl driver's retry was an `internal static` Polly pass-through with no seam; now `IRetryPolicy` with a hand-rolled core default — Polly leaves core. Additive, internal-only refactor.** `Infra.Executor` was a 14-line wrapper over `Polly.Policy.Handle<Exception>().RetryAsync(3)` with one call site, one fixed policy, and a dead synchronous `Retry<T>(Action)` overload (unused generic, zero callers). Three real adapter variations existed unnamed: the core default, a no-retry policy for deterministic tests, and consumer/satellite policies (Polly resilience pipelines, exponential backoff, navigation-aware retries). The `Handle<Exception>` catch-all also silently caught `OperationCanceledException`, so cancellation paid up to three wasted Spider invocations before propagating — a latent bug surfaced only when this seam was opened. Resolved: the **Retry policy** (`IRetryPolicy.ExecuteAsync<T>`) is a Tier-1 named seam; the core default `FixedAttemptsRetryPolicy` is hand-rolled (four attempts, no delay, cancellation cooperative by construction), so the `Polly` `PackageReference` leaves core (ADR-0009 dependency-light principle restored for this slice). Consumers wanting Polly's resilience pipeline wrap it in an `IRetryPolicy` adapter behind `ScraperEngineBuilder.WithRetryPolicy(...)`. Rejected with load-bearing reasons so future reviews don't re-suggest them: pure deletion (silently removes retries, doesn't fix the cancellation bug); a fix-in-place on `Executor` (leaves Polly + the static-helper shape + the hypothetical-seam problem); deepening into a broad *resilience* seam (one operation, one collaborator — narrow seam, not broad); pushing retry into `IPageLoader` (leaves parse/scheduler-edge failures uncovered); exposing `WithRetryPolicy` on `DistributedSpiderBuilder` (compounds with queue-level redelivery — the "double-retry storm" anti-pattern). See `docs/adr/0026-retry-policy-seam.md`.
- **The per-Job Spider shell leaked its result through side channels; termination was a thrown exception with no returned home — now a Job report value interpreted by a named Crawl driver. Deliberate, breaking.** `ISpider` was `CrawlAsync → List<Job>`; emitted data/callbacks escaped via `event`s on the concrete `Spider`, the crawl limit via a thrown `PageCrawlLimitException` run through the static `Executor` wrapper's `Polly.Handle<Exception>` fault-retry (retry-amplified; `Executor` itself was later retired in ADR-0026 in favour of the `IRetryPolicy` seam, see the previous bullet), dedup folded silently into the list — so no test ever constructed the real `Spider` and the live-site IntegrationTests were the only thing exercising orchestration. The Visited-link tracker was the root miscoupling: Crawl-global state wired as a per-Job collaborator, the shared cause of the limit exception, the racy discovery dedup (children filtered against the visited set but marked only when themselves crawled, under `Parallel.ForEachAsync`), and the distributed poison message (`WebReaper.AzureFuncs` had no driver to catch the throw → Service Bus dead-lettered the queue at the limit). Resolved: the **Crawl driver** is a named role with two adapters (in-process / distributed); the **Spider** is reduced to load → **Crawl step** → **Job report**; the **Visited-link tracker** moves to the driver as the single idempotency authority (atomic test-and-set gating dedup + limit + accounting); termination is the **Outstanding-work latch** (one seam, two adapters), `StopWhenDrained` subsumed. The page limit is now stated soft/best-effort and uniform across drivers (was accidental in-process, poison distributed). ADR 0001's closed `CrawlOutcome` is untouched — the **Job report** wraps it; the ADR-0001 closed-returned-value move re-applied one layer up. Rejected with load-bearing reasons so future reviews don't re-suggest them: a durable-workflow coordinator (β — vendor coupling + a second, unrelated completion mechanism), emergent queue-drain (a documented non-solution; "empty queue ≠ terminated" is a theorem, Kshemkalyani & Singhal Ch. 7), an exact distributed limit, and transport dedup-window as the sole correctness mechanism. See `docs/adr/0022-crawl-driver-and-outstanding-work-latch.md` and `research/distributed-crawl-termination.md`.
- **The core public surface was accidentally wide — public-because-a-test-or-satellite-binds-it, not public-because-it-is-contract — and its ~552 CS1591 was an open, unenforced backlog. Now the surface *is* the documented contract, drawn by the deletion test and enforced. Deliberate, breaking (9.0.0).** The split is not `*/Abstract`-public vs `*/Concrete`-internal: it is the **deletion test** per type — *named by a documented consumer / inherited by a satellite / part of the taught fluent API* ⇒ **Tier-1**, public, documented to the bar the codebase already set (`ScraperEngine`/`HttpPageLoadTransport` summaries); *reached only through a fluent builder method and named by nobody* ⇒ **Tier-2**, implementation, made `internal`. Tier-1 (Builders, every `*/Abstract` seam, the `Domain` model, `WebReaperJson`, `LoggerExtensions`, the payload-shell bases satellites inherit, the in-memory defaults the DIY-distributed pattern wires by hand, public exceptions) is documented; Tier-2 (the `File*`/`InMemory*` leaves, sinks/formats, parsers/loaders, `Spider`/`CrawlStep`/`InMemoryOutstandingWorkLatch`, `ValidatedProxyProvider`, the static `Executor` retry wrapper (since retired by ADR-0026 in favour of the `IRetryPolicy` seam — the default `FixedAttemptsRetryPolicy` is its successor and is also Tier-2), `ColorConsoleLogger`, the `Timer`/`Counter` log helpers) is `internal` — which clears ~half the backlog with **no filler doc and no NoWarn** (CS1591 does not fire on non-public members). `[InternalsVisibleTo]` targets the test + AOT-smoke assemblies only, never a shipped package (verified: no satellite-prod/Example/Misc names a Tier-2 type). The mechanical "remaining CS1591 == Tier-2" heuristic mis-classified four contract types the deletion test pulled back to Tier-1: `ScraperEngine` (the runtime object `BuildAsync` returns), `StaticProxySource`/`HttpProxyValidator` (the only built-in `WithValidatedProxies` inputs), and `SchemaContentParser<TNode>` (the ADR-0002 custom-backend reuse vehicle). Enforcement is project-wide `WarningsAsErrors=CS1591` — an undocumented public member now fails the build, so the backlog cannot re-accumulate (it accumulated *because* nothing failed). Rejected with load-bearing reasons: document-everything-to-zero (filler on types no one calls; destroys the signal), bulk `NoWarn` on core (reinstates the invisible backlog), a factory to keep the surface "concrete-free" (a shallow module — interface as complex as its one-line body), and a non-breaking 8.1.0/9.0.0 split. Core CS1591 went 294 → 0, contract-enforced. See `docs/adr/0023-core-doc-contract.md`.
