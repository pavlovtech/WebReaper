# WebReaper

The domain language of WebReaper — a declarative, parallel web crawler. This file names the concepts a contributor must share to reason about the crawl correctly; it is the reference the architecture review defers to.

## Language

### Crawl topology

**Crawl**:
One end-to-end run of the scraper over a site, seeded from the configured start URLs.
_Avoid_: scrape, scan, session.

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
A backend-agnostic seam that persists and fetches one opaque UTF-8 string under a key, last-write-wins, with `null` meaning absent; its four adapters (in-memory, file, Redis, Mongo) are the only place a persistence mechanism lives.
_Avoid_: repository, cache, document DB, config storage.

**Payload shell**:
The thin module above a **keyed blob store** that owns one payload's serialization grammar and quirks (the config shell owns `TypeNameHandling.Auto`; the cookie shell owns `CookieContainer ↔ CookieCollection`) and decides what *absent* means for that payload.
_Avoid_: storage, provider, serializer.

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

## Relationships

- A **Crawl** processes many **Job**s.
- A **Job** carries a **Selector chain**; the chain's length and head derive the **Page category** (it is not stored on the Job).
- A **Crawl step** maps one **Job** to one **Crawl outcome**.
- A **Target page** outcome produces one **ParsedData**, emitted to every **Sink**.
- A file **Sink** is one **File sink drain** plus one **Row format**; the drain is shared, the format is the only per-format variation.
- A **Transit page** **advance**s the selector chain; a **Page with pagination** **retain**s it.
- A **Schema fold** interprets a **Schema** by calling one **Node backend**; the backend yields **raw value**s, the fold applies **typed coercion**.
- A **Payload shell** serializes one payload and delegates storage to one **Keyed blob store**; the store never knows which payload it holds.
- The **Spider** calls one **Page loader** with a **PageRequest**; the loader dispatches on **PageType** to one **Load transport**, which applies the optional proxy its own way.

## Example dialogue

> **Dev:** "When a page-with-pagination crawl runs, do the item Jobs and the next-page Jobs both keep the same selector chain?"
> **Domain expert:** "No. The item Jobs **advance** — the listing selector is consumed, so they're target pages now. The next-page Jobs **retain** the chain, because page 2 of the listing is the same step, not a deeper one."

> **Dev:** "The Mongo config store used to keep a queryable BSON document; now it's a string blob — isn't that a regression?"
> **Domain expert:** "No. WebReaper only ever fetches a whole config by key, never queries inside it. The **keyed blob store** holds an opaque string; the `TypeNameHandling.Auto` that round-trips `PageAction.Parameters` (`object[]`) and the `ImmutableQueue` selector chain lives in the config **payload shell**, not the store. The BSON shape was never load-bearing."

> **Dev:** "How does the Spider decide between an HTTP fetch and a headless browser?"
> **Domain expert:** "It doesn't — it builds a **PageRequest** and hands it to the one **page loader**. The loader reads **PageType** and dispatches to the HTTP or browser **load transport**. Whether a proxy is used, and how it's applied, is the transport's business, not the Spider's."

## Flagged ambiguities

- **Selector-chain handling of pagination vs following was implicit.** In `Spider.CrawlAsync` one call site passed the dequeued chain and another the original chain, with nothing naming the difference. Resolved structurally: the **Crawl step** returns a **Crawl outcome** whose `Followed.Next` and `Paginated.Items` carry the **advance**d chain and whose `Paginated.NextPages` carries the **retain**ed chain — the two rules are now distinct named fields, not two look-alike call sites (see `docs/adr/0001-crawl-outcome-closed-sum.md`).
- **"Page type" vs "page category".** Resolved: **page category** = Target / Transit / Pagination, derived from the selector chain. **PageType** is the load mode (Static vs Dynamic, i.e. HTTP vs Puppeteer). Distinct concepts — never conflate them.
- **The HTML-vs-JSON untyped-leaf difference was accidental duplication; it is now a deliberate, pinned property.** An untyped leaf is the **raw value** verbatim: HTML yields a string, JSON keeps its native number/bool. This is intentional (JSON-endpoint users depend on native types) and is the *only* legitimate behavioural difference between backends — it rides on `ExtractRaw`'s return type, not copied code, and is pinned cross-backend in `SchemaFoldTests`. Do not "unify" it (see `docs/adr/0002-schema-fold-and-node-backend-seam.md`).
- **Previously-divergent log/selector behaviour is now uniform — by design, not regression.** The missing-node and parsing-error log messages were textually different per backend, and the HTML single-value path tolerated a missing selector where the list paths did not. The fold makes all three uniform. Observable outcomes (field left empty/unset, parse not aborted) are unchanged; only the divergent log text and the single-value selector-miss mechanism were unified.
- **The config/cookie persistence stores were eight near-duplicate classes; the duplication had drifted into real bugs. Now one keyed blob store + per-payload shells — deliberate, not regression.** Mongo stores an opaque `{id, blob}`, not a queried BSON projection (never queried — do not "restore" it); the missing-value policy is uniform (`null` ⇔ absent at the store; the config shell throws a typed not-found, the cookie shell returns an empty `CookieContainer`), replacing the File adapter's `NullReferenceException` and the silent-null divergence; `PutAsync` is upsert-by-key, fixing the Mongo append/read-oldest bug; `ScraperConfig` now round-trips with `TypeNameHandling.Auto` through *every* backend (Redis was silently lossy); in-memory storage now round-trips through the shell's serializer like every other backend (was: held the live object), so the cheap path exercises the same serialization. `RedisBase`'s process-static single-multiplexer bug is fully resolved: `RedisBase` is retired and all four Redis adapters (blob store, scheduler, sink, visited-link tracker) share one `RedisConnectionPool` — one multiplexer per connection string, no statics. See `docs/adr/0003-keyed-blob-store-and-payload-shells.md` and `docs/adr/0005-redis-connection-pool.md`.
- **The Static/Dynamic loader split was two single-adapter seams plus a copy-pasted requester triad and Puppeteer pair; the proxy/no-proxy choice had no home. Now one `IPageLoader` + two `IPageLoadTransport`s — breaking, deliberate.** `IStaticPageLoader` had exactly one implementation; the proxy decision was re-made in the builder branch, the requester triad, and the Puppeteer pair, with bugs drifted into the copies. The Spider no longer dispatches by load mode (that home moved behind the **page loader**); `IStaticPageLoader`, `IBrowserPageLoader`, `IPageRequester` + its three impls, and the two Puppeteer classes are removed (major SemVer). Fixed by construction, deliberately: the non-proxy static path now actually applies stored cookies (the handler was previously built *before* the cookie container was set); one canonical User-Agent (the triad had two by copy-drift); one canonical browser navigation wait, `Networkidle2` (the Puppeteer pair had `DOMContentLoaded` vs `Networkidle2` by accidental drift); the never-constructed, buggy `ProxyPageRequester` is gone. Out of scope, preserved as-is: the browser page-action table still handles only four of six `PageActionType`s (a missing feature, not this duplication deepening). See `docs/adr/0004-one-page-loader-transport-seam.md`.
- **The distributed scheduler's `Job` round-trip is serialize/deserialize-asymmetric — named, not yet fixed.** `RedisScheduler` writes a `Job` with `TypeNameHandling.None` and reads it back with default settings, so a `Job`'s `ImmutableQueue<LinkPathSelector>` and `PageAction.Parameters` (`object[]`) lose the type metadata they need to rematerialise — the same asymmetry ADR 0003 fixed for the config payload. The `RedisBase` retirement (ADR 0005) preserved this verbatim rather than widen its scope; it is a distinct future candidate, not a regression introduced there. See `docs/adr/0005-redis-connection-pool.md`.
- **The two file sinks were one buffered drain copy-pasted with drifted bugs; now one `BufferedFileSink` + an `IFileSinkFormat` quirk — deliberate, not regression.** Cleanup and directory creation are now eager and unconditional (deterministic even for a zero-row crawl; old CSV kept stale data and never created the directory, old JSON-lines created it only when cleaning); one consumer is started once, bound to the first emit's token (old JSON-lines used `CancellationToken.None` with dead re-init code, old CSV could double-spawn it under concurrent first emits). Observable file content is unchanged. Out of scope, preserved verbatim: one `File.AppendAllTextAsync` per row and no consumer flush/dispose — a *shared* property of the old code, a separate future candidate. See `docs/adr/0006-file-sink-buffered-drain.md`.
- **XPath is a third `ISchemaBackend`, and it deliberately does not copy the CSS backend's `src`→`title` rewrite — by design, not inconsistency.** Discussion #17 asked for XPath/RegEx; XPath shipped as `AngleSharpXPathSchemaBackend` over the same AngleSharp DOM (the ADR 0002 seam used as intended, fold unduplicated). The CSS backend's requested-`src`→`title` rewrite is a quarantined legacy quirk of *that* backend; per ADR 0002 quirks are backend-local, so the XPath backend returns the attribute asked for (the only behavioural difference, pinned by a test). RegEx selectors were declined: a regex over markup has no node scope and cannot satisfy the `SelectMany`/`SelectOne` contract. Link discovery stays CSS (out of scope, as with the JSON backend). See `docs/adr/0007-xpath-schema-backend.md`.
