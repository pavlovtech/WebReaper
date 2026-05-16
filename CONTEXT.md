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
The per-document-shape seam the fold calls (`ISchemaBackend<TNode>`): parse a root, select-many, select-one, extract a leaf's **raw value**. HTML/CSS and JSON/JSONPath are two backends; the seam is the only place document-specific quirks live.
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

## Relationships

- A **Crawl** processes many **Job**s.
- A **Job** carries a **Selector chain**; the chain's length and head derive the **Page category** (it is not stored on the Job).
- A **Crawl step** maps one **Job** to one **Crawl outcome**.
- A **Target page** outcome produces one **ParsedData**, emitted to every **Sink**.
- A **Transit page** **advance**s the selector chain; a **Page with pagination** **retain**s it.
- A **Schema fold** interprets a **Schema** by calling one **Node backend**; the backend yields **raw value**s, the fold applies **typed coercion**.

## Example dialogue

> **Dev:** "When a page-with-pagination crawl runs, do the item Jobs and the next-page Jobs both keep the same selector chain?"
> **Domain expert:** "No. The item Jobs **advance** — the listing selector is consumed, so they're target pages now. The next-page Jobs **retain** the chain, because page 2 of the listing is the same step, not a deeper one."

## Flagged ambiguities

- **Selector-chain handling of pagination vs following was implicit.** In `Spider.CrawlAsync` one call site passed the dequeued chain and another the original chain, with nothing naming the difference. Resolved structurally: the **Crawl step** returns a **Crawl outcome** whose `Followed.Next` and `Paginated.Items` carry the **advance**d chain and whose `Paginated.NextPages` carries the **retain**ed chain — the two rules are now distinct named fields, not two look-alike call sites (see `docs/adr/0001-crawl-outcome-closed-sum.md`).
- **"Page type" vs "page category".** Resolved: **page category** = Target / Transit / Pagination, derived from the selector chain. **PageType** is the load mode (Static vs Dynamic, i.e. HTTP vs Puppeteer). Distinct concepts — never conflate them.
- **The HTML-vs-JSON untyped-leaf difference was accidental duplication; it is now a deliberate, pinned property.** An untyped leaf is the **raw value** verbatim: HTML yields a string, JSON keeps its native number/bool. This is intentional (JSON-endpoint users depend on native types) and is the *only* legitimate behavioural difference between backends — it rides on `ExtractRaw`'s return type, not copied code, and is pinned cross-backend in `SchemaFoldTests`. Do not "unify" it (see `docs/adr/0002-schema-fold-and-node-backend-seam.md`).
- **Previously-divergent log/selector behaviour is now uniform — by design, not regression.** The missing-node and parsing-error log messages were textually different per backend, and the HTML single-value path tolerated a missing selector where the list paths did not. The fold makes all three uniform. Observable outcomes (field left empty/unset, parse not aborted) are unchanged; only the divergent log text and the single-value selector-miss mechanism were unified.
