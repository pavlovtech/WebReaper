# The Spider shell takes its run-scoped inputs at construction; `IScraperConfigStorage` leaves the shell

## Status

**Accepted — implementation complete** (2026-05-22; landed on branch
`adr-0034-spider-config-at-construction` off `origin/master`). Ninth ADR
of the `/improve-codebase-architecture` review wave (after ADR-0026
through ADR-0033, PRs #82-90, all merged), and **candidate #3 of the
2026-05-22 review**. Breaking: `DistributedSpiderBuilder` — a Tier-1
public seam (ADR-0009/0023) — loses `WithConfigStorage` /
`WithFileConfigStorage`, and `BuildSpider()` gains a required
`ScraperConfig` parameter. Folds into the unreleased 10.0.0 wave the
user is batching.

## Context

The **Spider** is the per-Job I/O shell (ADR-0022): load one Job's page,
run the **Crawl step**, return a **Job report** — nothing else. It is
deliberately shallow. Its constructor, though, takes three
dependencies — `ICrawlStep`, `IPageLoader`, and `IScraperConfigStorage` —
and `CrawlAsync` opens with:

```csharp
var config = await ScraperConfigStorage.GetConfigAsync();
```

It fetches the entire immutable `ScraperConfig` **on every Job** to read
exactly two fields:

- `config.Headless` — folded into the `PageRequest` handed to the loader.
- `config.ParsingScheme` — passed to `CrawlStep.StepAsync`.

Both are **run-scoped**: `ConfigBuilder.Build()` constructs the
`ScraperConfig` once, and it is immutable for the whole crawl. The
Spider is run-scoped too — one Spider per crawl. Yet it re-reads those
two fields once per page.

**The deletion test.** Delete `IScraperConfigStorage` from the Spider's
constructor. The two values must then be supplied another way — and they
are already in hand: `ScraperEngineBuilder.BuildAsync` builds the
`ScraperConfig` object *before* it persists it, and `ScraperEngine.RunAsync`
reads it once at the top of the run. The dependency does not reappear
*across N callers*; it relocates to a **single construction site** — the
builder that wires the Spider. That is the signature of a **pass-through
dependency**: the Spider holds `IScraperConfigStorage` not because being
a per-Job shell requires it, but to pluck two values it could be handed
directly.

**What the per-Job refetch costs:**

- *In-process.* The default `InMemoryScraperConfigStorage` makes
  `GetConfigAsync()` a dictionary lookup — cheap. The cost is not
  latency; it is **interface width**. A maintainer or AI reading
  `Spider` sees three constructor dependencies and must trace
  `CrawlAsync` to discover that one of them exists only to read two
  immutable fields. The shell's interface advertises more than the
  shell's job.
- *Distributed.* `IScraperConfigStorage` is precisely the seam that
  exists so a stateless worker recovers the crawl definition from
  **remote** storage (Redis, Mongo — see the seam's own doc: *"how a
  stateless worker recovers the crawl definition it was never passed
  in-process"*). There, `GetConfigAsync()` is a network round-trip — and
  the Spider does it on **every page**.
- *The implicit contract is a footgun.* "The Spider fetches its config
  at crawl time" is invisible at the build site. The flagship
  distributed example — `Examples/WebReaper.AzureFuncs/WebReaperSpider.cs`
  — builds its worker spider with
  `new DistributedSpiderBuilder().WithLogger(log).BuildSpider()` and
  **never calls `.WithConfigStorage(...)`**. The worker silently gets
  the empty default `InMemoryScraperConfigStorage`; the first Job's
  `CrawlAsync` throws `ConfigNotFoundException` ("CreateConfigAsync must
  run before GetConfigAsync") — a crawl-time failure whose message
  points at the wrong thing. The contract is implicit enough that the
  canonical example gets it wrong.
- *Test surface.* `SpiderTests` — the only suite that constructs the
  real `Spider` — must build a `FakeConfig`, a hand-written
  `IScraperConfigStorage` test double, for every test, purely to feed
  the Spider two values.

`ICrawlStep`'s own contract states the principle the Spider violates:
*"No I/O behind this seam — no page loaders, visited-link tracker,
sinks, scheduler, **or config storage**."* The crawl step is
config-storage-free; the Spider shell around it is the lone place config
storage is read at crawl time.

This is a shallow shell carrying a pass-through dependency and hidden
per-page I/O. The deepening is to **narrow the shell's interface**: hand
it the two run-scoped values once, at construction, and remove
`IScraperConfigStorage` entirely.

## Decision

Three moves.

### 1. The Spider takes its two run-scoped inputs at construction

`Spider`'s constructor changes from `(ICrawlStep, IPageLoader,
IScraperConfigStorage)` to `(ICrawlStep, IPageLoader, bool headless,
Schema? parsingScheme)`. `CrawlAsync` drops the `GetConfigAsync()` call
and reads the two constructor-supplied fields. `IScraperConfigStorage`
leaves the Spider's interface entirely; `ISpider.CrawlAsync` — the
run-time interface — is **unchanged**.

**Why the two values, not the whole `ScraperConfig`** (considered
option (a)): the shell touches two of the record's nine fields. A
constructor that declares `ScraperConfig` says *"I need the entire crawl
definition"*; one that declares `bool headless, Schema? parsingScheme`
declares **exactly** what the shell uses. The narrower interface is the
honest one — and it is what lets `SpiderTests` drop its
`IScraperConfigStorage` double and construct the Spider from the values
directly.

**Why `ParsingScheme` is not pushed down into `CrawlStep`** (considered
option (b)): tempting — then the Spider would hold only `Headless`. But
`ICrawlStep`'s contract is *"pure and deterministic in (job, document,
schema)"* (ADR-0002): the schema is deliberately a **parameter** of
`StepAsync`, not `CrawlStep` state. Making `CrawlStep` hold the schema
would make the step stateful in it and contradict that contract. The
Spider keeps passing `schema` to `StepAsync` — that is `ICrawlStep`'s
designed interface, not a pass-through smell. **`CrawlStep` and
`ICrawlStep` are untouched.**

### 2. `SpiderBuilder` is handed the config; `IScraperConfigStorage` leaves it too

The internal `SpiderBuilder` — the shared runtime-component builder both
public builders compose — loses `WithConfigStorage`,
`WithFileConfigStorage`, and its `ScraperConfigStorage` property, and
gains one `WithConfig(ScraperConfig)`. `Build()` projects the two
values: `new Spider(crawlStep, PageLoader, config.Headless,
config.ParsingScheme)`. `IScraperConfigStorage` is no longer a
`SpiderBuilder` concern at all.

### 3. Config storage becomes purely the *driver's* concern

After moves 1–2 the two public builders differ only in **how they
obtain the `ScraperConfig`** to hand `SpiderBuilder`:

- `ScraperEngineBuilder.BuildAsync` already builds the `ScraperConfig`
  (`ConfigBuilder.Build()`) and holds the object — it passes it straight
  to `SpiderBuilder.WithConfig(config)`, then persists it via
  `CreateConfigAsync`. No fetch; the object it persists is the object it
  wires.
- `DistributedSpiderBuilder.BuildSpider()` becomes
  **`BuildSpider(ScraperConfig config)`** — a required terminal
  parameter. `WithConfigStorage` / `WithFileConfigStorage` are removed.
  The distributed worker fetches its config from the shared store the
  start endpoint persisted to, and hands the object to the build
  terminal.

`IScraperConfigStorage` **stays** a seam — the in-process engine still
persists through `CreateConfigAsync`, the distributed start endpoint
(`ScraperEngineBuilder.Build` plus a satellite config store) still
writes it, and a distributed worker still reads it. What changes is
**whose dependency it is**: the **Crawl driver's** (in-process
`ScraperEngine`, or the consumer-authored distributed worker), never the
**shell's**. ADR-0009's "the distributed driver is consumer-authored"
posture is upheld and sharpened — the worker now drives its own config
acquisition, exactly as ADR-0033 had it drive its own
`InitializeAsync()`.

**Why a required terminal parameter, not a `WithConfig` fluent step:**
config is **required** — the shell cannot work without it — unlike the
optional `With*` tweaks (`WithLogger`, `WithProxies`, …). A `WithConfig`
fluent step can be forgotten, yielding a null config and an NRE.
`BuildSpider(ScraperConfig)` makes "build a worker spider with no
config" a **compile error** — the ADR-0025 "misbuild is structurally
unrepresentable" instinct, applied to the distributed seam. It also
retires the current footgun: there is no longer a default
`InMemoryScraperConfigStorage` to silently fall through to.

### Bounded scope — what this does NOT change

- **`ISpider.CrawlAsync`.** Unchanged. The change is to the Spider's
  *constructor*, not its run-time interface. Every `ISpider` consumer —
  the Crawl driver, the test `ScriptedSpider` — is unaffected.
- **`ScraperConfig`, `PageRequest`, `ICrawlStep`,
  `IScraperConfigStorage`.** All unchanged. `ScraperConfig` stays the
  one immutable crawl definition; the Spider receives a *projection* of
  it, not a new type.
- **The in-process engine's own config read.** `ScraperEngine.RunAsync`
  reads `ScraperConfig` once per run and genuinely uses most of the
  record (start URLs, the selector chain, the page limit, the blacklist,
  the stop rule, the start page type and actions). The Crawl driver is
  *allowed* to own config storage; the shell is not. The driver's
  once-per-run read is correct as is and out of scope.
- **Observable crawl behaviour.** Byte-identical — same `Headless`, same
  `ParsingScheme`, same `PageRequest`, same `JobReport`. Only *when* the
  two values are read changes: at construction, not per Job.
- **The distributed driver stays consumer-authored** (ADR-0009).
  `WebReaper.AzureFuncs` is *updated* to fetch the config and pass it;
  no shared config-acquisition module is shipped — the ADR-0032/0033
  posture.

## Considered options

### (a) The Spider takes the whole `ScraperConfig`

`Spider(ICrawlStep, IPageLoader, ScraperConfig)`. The smallest diff —
`CrawlStep` and `SpiderBuilder` barely change — and `ScraperConfig` is
*the* domain object. **Rejected:** the constructor would advertise a
dependency on the entire crawl definition for a shell that reads two of
nine fields — a wide interface for a deliberately shallow module. The
narrow constructor is the deeper interface, and it is what shrinks the
test surface (no `ScraperConfig` to build, no `IScraperConfigStorage` to
fake).

### (b) Push `ParsingScheme` into `CrawlStep`; the Spider holds only `Headless`

`CrawlStep` would hold the `Schema?` and `StepAsync` would drop its
`schema` parameter, leaving the Spider with a single `bool`.
**Rejected:** it contradicts `ICrawlStep`'s ADR-0002 contract — *"pure
and deterministic in (job, document, schema)"*. The schema is a
parameter **by design**: the crawl step is a pure function *of* the
schema, not a stateful object configured *with* it. Holding it as
`CrawlStep` state trades a real architectural invariant for the cosmetic
win of one fewer constructor argument.

### (c) Keep `IScraperConfigStorage` on the Spider, memoize the first `GetConfigAsync()`

Cache the first fetch so later Jobs reuse it. **Rejected:** it treats
the symptom (repeated I/O) and leaves the cause (the wrong dependency).
The Spider still constructs against a storage seam it does not need; the
implicit at-crawl-time contract and the distributed footgun (a forgotten
`.WithConfigStorage()` → `ConfigNotFoundException`) survive intact; and
`SpiderTests` still needs the `IScraperConfigStorage` double.
Memoization narrows nothing.

### (d) `DistributedSpiderBuilder` keeps `.WithConfigStorage(storage)`, gets an async `BuildSpiderAsync()`

The builder retains the storage handle and `BuildSpiderAsync()` awaits
`GetConfigAsync()` once. **Rejected:** sync→async is itself a breaking
signature change, so it buys no compatibility — and it **re-hides the
fetch inside the builder**. A worker that calls `BuildSpiderAsync()` per
message refetches per message: the exact hidden-per-call-I/O shape this
ADR removes from the Spider, merely relocated to the builder. A required
`ScraperConfig` parameter makes acquisition explicit and the worker's
own — and therefore hoistable: the worker can fetch once per cold start
and reuse, which the builder-encapsulated form actively obscures.

### (e) Split `ScraperConfig` into a driver-config and a shell-config record

`ScraperConfig` genuinely serves two audiences — the **Crawl driver's**
crawl-loop fields (start URLs, selector chain, page limit, stop rule,
blacklist, start page type and actions) and the **shell's** per-page
fields (headless, parsing schema). A split would let each receive only
its own record. **Rejected as out of scope:** `ScraperConfig` is
serialized and round-tripped through every config-storage backend
(ADR-0003/0008); splitting the record is a far larger, breaking change
than narrowing one constructor. Noted as a possible future candidate.
This ADR hands the Spider a *projection* of the existing record (two
values), introducing no new type.

## Consequences

- **The Spider's interface is narrower and honest.** Its constructor
  declares exactly the two run-scoped values the shell applies;
  `IScraperConfigStorage` and the per-Job `GetConfigAsync()` are gone
  from it. A reader sees the shell's true inputs at construction, not by
  tracing `CrawlAsync`.
- **No hidden per-page I/O.** In distributed mode the per-page config
  round-trip is removed from the shell; acquisition becomes one
  explicit, hoistable call the worker owns.
- **Config storage is the driver's concern, uniformly.** Both Crawl
  drivers acquire the `ScraperConfig` and hand it down; neither the
  shell nor `SpiderBuilder` references `IScraperConfigStorage`. The seam
  survives, owned where the principle in `ICrawlStep`'s doc already put
  it.
- **`DistributedSpiderBuilder` is a breaking change.** `WithConfigStorage`
  / `WithFileConfigStorage` are removed; `BuildSpider()` becomes
  `BuildSpider(ScraperConfig)`. A distributed worker must fetch its
  config and pass it. Tier-1 public (ADR-0009/0023) → SemVer **major** →
  folds into the unreleased 10.0.0 wave. `ScraperEngineBuilder`'s own
  `WithConfigStorage` / `WithFileConfigStorage` are **unchanged** — the
  engine path and the satellite config-store extensions
  (`WithRedisConfigStorage`, the `WriteToMongoDb` family) are
  unaffected.
- **The distributed footgun is retired.** "Build a worker spider with no
  config" was a build-time silence followed by a crawl-time
  `ConfigNotFoundException`; it is now a compile error at the build
  site.
- **The test surface shrank.** `SpiderTests` drops `FakeConfig` — its
  hand-written `IScraperConfigStorage` double — and constructs the real
  `Spider` from `(ICrawlStep, IPageLoader, headless, schema)`. The
  `Shell_never_throws_to_signal_the_crawl_limit` test is removed: it
  pinned that the shell, though handed a `ScraperConfig` carrying
  `PageCrawlLimit`, ignored the limit — a property that is now
  **structural** (the shell has no config and no limit input to
  ignore). A new test pins the new construction-time behaviour: the
  `headless` flag the Spider is constructed with is the one folded into
  the `PageRequest`.
- **`Examples/WebReaper.AzureFuncs` is updated.** `WebReaperSpider`
  injects `IScraperConfigStorage`, fetches the `ScraperConfig` once via
  a `Lazy<Task<ScraperConfig>>` (one round-trip across every message the
  function instance handles — the `Lazy<Task>` idiom ADR-0033 used for
  warm-up), and passes it to `BuildSpider`. The example now demonstrates
  the hoist instead of hiding a per-page fetch.
- **CONTEXT.md** — the **Distributed spider builder** term is corrected
  (the worker no longer "reads config from shared storage at crawl
  time"); the **Spider** term gains its run-scoped-inputs-at-construction
  clause; a new "Flagged ambiguities" bullet records the decision.

## Implementation

Landed on `adr-0034-spider-config-at-construction`:

1. **`WebReaper/Core/Spider/Concrete/Spider.cs`** — constructor
   `(ICrawlStep, IPageLoader, bool headless, Schema? parsingScheme)`;
   `CrawlAsync` reads the two ctor fields, no `GetConfigAsync()`; the
   `IScraperConfigStorage` dependency removed.
2. **`WebReaper/Builders/SpiderBuilder.cs`** — `WithConfigStorage` /
   `WithFileConfigStorage` / the `ScraperConfigStorage` property
   replaced by one `WithConfig(ScraperConfig)`; `Build()` projects
   `config.Headless` + `config.ParsingScheme` into the `Spider`.
3. **`WebReaper/Builders/ScraperEngineBuilder.cs`** — `BuildAsync`
   builds the config first, then `SpiderBuilder.WithConfig(config)`;
   `WithFileConfigStorage` no longer delegates to `SpiderBuilder` (it
   still sets the engine's own `ConfigStorage`). `WithConfigStorage`
   unchanged.
4. **`WebReaper/Builders/DistributedSpiderBuilder.cs`** —
   `WithConfigStorage` / `WithFileConfigStorage` and the `_configStorage`
   field removed; `BuildSpider()` → `BuildSpider(ScraperConfig config)`
   (null-checked); class and method docs rewritten.
5. **`Examples/WebReaper.AzureFuncs/WebReaperSpider.cs`** — injects
   `IScraperConfigStorage`, fetches the config via
   `Lazy<Task<ScraperConfig>>`, passes it to `BuildSpider`.
6. **`WebReaper.Tests/WebReaper.UnitTests/SpiderTests.cs`** — `FakeConfig`
   removed; the Spider built from `(crawl step, loader, headless,
   schema)`; `FakeLoader` captures the `PageRequest`; the limit test
   replaced by a headless-projection test.
7. **`WebReaper.Tests/WebReaper.UnitTests/BuildSpiderTests.cs`** — the
   `DistributedSpiderBuilder` test passes a `ScraperConfig` to
   `BuildSpider`.
8. **`CONTEXT.md`** — **Distributed spider builder** and **Spider**
   terms updated; one new "Flagged ambiguities" bullet.

### Guardrails (run on the branch tip)

- `dotnet build WebReaper.sln` — **0 errors**; 19 warnings, all
  pre-existing — **unchanged in count** from the ADR-0033 guardrail (19).
  The only delta to the warning *set* is a one-for-one swap: the new
  `SpiderBuilder.Config` property (CS8618 non-nullable-field) replaces
  the removed `SpiderBuilder.ScraperConfigStorage` property (also
  CS8618). Core's `WarningsAsErrors=CS1591` stays green — the new public
  `DistributedSpiderBuilder.BuildSpider(ScraperConfig)` carries its XML
  docs; `SpiderBuilder.WithConfig` is on an internal type.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — **163/163 pass**
  (unchanged total: `SpiderTests` drops the now-vacuous
  `Shell_never_throws_to_signal_the_crawl_limit` — the shell has no
  config and no limit input to ignore — and adds
  `The_constructed_headless_flag_is_folded_into_every_page_request`;
  `BuildSpiderTests` passes a `ScraperConfig` to `BuildSpider`).
- `Examples/WebReaper.AzureFuncs` — the updated consumer-authored
  distributed driver — compiles in the whole-solution build, as do every
  satellite and `Misc/` project. No satellite code is touched:
  `IScraperConfigStorage` and its adapters are unchanged; the change is
  the Spider's constructor and the two builders. The network-backed
  satellite suites (`Redis` / `Mongo` / `Cosmos` / `AzureServiceBus` /
  `Sqlite`) and the live-site `WebReaper.IntegrationTests` run on CI.

## References

- ADR-0022 — the per-Job Spider shell this ADR narrows; "the shell
  reports, the driver decides". Config storage is now confirmed the
  driver's, not the shell's.
- ADR-0009 — the consumer-authored distributed driver and the
  `DistributedSpiderBuilder` seam; the worker now drives config
  acquisition itself.
- ADR-0025 — "misbuild is structurally unrepresentable"; the required
  `BuildSpider(ScraperConfig)` parameter applies that instinct to the
  distributed seam.
- ADR-0002 — `ICrawlStep` is pure and deterministic in (job, document,
  schema); why `ParsingScheme` stays a `StepAsync` parameter and
  `CrawlStep` is untouched.
- ADR-0033 — the immediately prior precedent: the distributed driver
  drives its own `InitializeAsync()`; here it drives its own config
  fetch, with the same `Lazy<Task>` hoist.
- ADR-0023 — the Tier-1 / Tier-2 split: `DistributedSpiderBuilder` is a
  Tier-1 public seam, so removing its config-storage methods is a
  documented breaking change.
