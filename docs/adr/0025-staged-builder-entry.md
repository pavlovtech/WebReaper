# A scrape begins with a Crawl seed — make "build with no start URLs or no schema" unrepresentable, not documented

## Status

**Accepted — implemented (10.0.0 wave).** Origin: an
`/improve-codebase-architecture` review (candidate #4, the last of four; #1–#3
shipped as ADR-0022), a grilling loop, then a source-verification pass that
*sharpened* it (see *The verification that reshaped the decision*). The grill's
open fork — does staging cover `ConfigBuilder`, used standalone, or only
`ScraperEngineBuilder`? — was put to the maintainer and resolved **R-complete:
stage both**. Implementation then surfaced one further fork the design prose had
glossed, also resolved with the maintainer (see *Implementation note: the
trilemma*): the structural guarantee, the seedless `BuildSpider()`, and zero
satellite ripple cannot all hold with a single type, so the ADR-0009
distributed-worker reduced shell became its own public type,
`DistributedSpiderBuilder` — the "two seams, not one bug" framing made concrete.
Shipped: whole-solution build 0 errors, 94 unit + 27 satellite tests green,
Native-AOT smoke ALL PASS.

## Context

`ScraperEngineBuilder` and `ConfigBuilder` are the front door of the library.
The project's whole value proposition — its maintainer's stated first priority —
is that the call site is easy to read and hard to misuse. Today the front door
has a runtime trap:

- [`ConfigBuilder.Build()`](../../WebReaper/Builders/ConfigBuilder.cs#L208-L215)
  `throw`s `InvalidOperationException` if start URLs were never set (L211) or a
  schema was never set (L214). Its own doc comment says *"Builder order
  matters — configure before calling this."* — a comment that exists only
  because the type can't enforce its own contract.
- [`BuildAsync()`](../../WebReaper/Builders/ScraperEngineBuilder.cs#L477-L488)
  calls `ConfigBuilder.Build()` (L480) and inherits the throw.
- `CLAUDE.md` gotcha #3 documents the trap in prose for every future reader.

A documented gotcha guarding an ordering rule the type system could carry is the
exact smell every prior WebReaper ADR exists to kill: ADR-0001 made an invalid
crawl outcome unrepresentable (closed sum); ADR-0022 made a non-terminating
crawl unrepresentable (termination is a value, never a thrown exception);
ADR-0005/0008/0023 each replaced a duplication-with-drift with one owner. The
signature move of this codebase is *turn a runtime/implicit failure into a
structurally impossible one* — and it has not yet been applied to the front
door it most affects.

Apply the project's own deletion test to the two `throw`s. Delete them and ask
what reappears: a call site that compiles, runs, hits the network setup, and
fails *late* with no start URL or no schema — the worst place to learn it, far
from the mistake. The throws are load-bearing; they are catching a real error.
That is precisely the signal that the *type*, not a runtime check plus a
`CLAUDE.md` paragraph, should own the rule.

There are three real client shapes, all verified in `Examples/`:

1. **In-process** (`WebReaper.ConsoleApplication`):
   `new ScraperEngineBuilder().Get(...).Parse(...)….BuildAsync()`.
2. **Distributed worker-service**
   (`DistributedScraperWorkerService/ScrapingWorker.cs`):
   `await new ScraperEngineBuilder().WithLogger(...).Get(...).Parse(...)
   .TrackVisitedLinksInRedis(...)….BuildAsync()` — full config in-builder, a
   satellite extension interleaved.
3. **DIY-distributed split** (`WebReaper.AzureFuncs`, the ADR-0009 /
   ADR-0022-slice-4 pattern):
   - [`StartScraping.cs:55-72`](../../Examples/WebReaper.AzureFuncs/StartScraping.cs#L55-L72)
     — the *start endpoint* builds a `ScraperConfig` via **`ConfigBuilder`
     directly** (`new ConfigBuilder().Get(...).WithScheme(...).Build()`, L70)
     and persists it to shared config storage.
   - [`WebReaperSpider.cs:52-55`](../../Examples/WebReaper.AzureFuncs/WebReaperSpider.cs#L52-L55)
     — the *worker* calls `new ScraperEngineBuilder().WithLogger(log)
     .WithLinkTracker(LinkTracker).BuildSpider()` with **no start URLs and no
     schema, by design**: it is config-agnostic; its spider reads the
     `ScraperConfig` from shared storage at crawl time.

## The verification that reshaped the decision

The grilled design proposed gating *all* terminals — including
[`BuildSpider()`](../../WebReaper/Builders/ScraperEngineBuilder.cs#L462-L466) —
behind the new prefix, on the belief that `BuildSpider()`'s skipping the
validation `BuildAsync()` performs was a latent asymmetry to close. Reading the
source retired that belief:

- `BuildSpider()` is `SpiderBuilder.WithConfigStorage(...); return
  SpiderBuilder.Build();`. It **never calls `ConfigBuilder.Build()`**. It has no
  unset-config throw to "fix." It is not a validate-skipping bug; it is the
  ADR-0009 reduced-shell seam for a config-agnostic distributed worker.
- Gating it behind the seed would force every distributed worker
  (`WebReaperSpider.cs` is the canonical one) to declare start URLs and a schema
  it structurally never uses — silently regressing a documented, shipped
  pattern. That is the same move ADR-0024 explicitly refused to make to
  single-satellite releases.

So the friction lives *only* on the config-owning path (`ConfigBuilder.Build()`
and its caller `BuildAsync()`). The fix stages that path. `BuildSpider()` stays
seedless — and that is **correct, not asymmetry**. The apparent asymmetry
dissolves the right way: two seams, not one bug. This is a strict improvement —
the design stops fighting the ADR-0009 registration seam.

## Decision

**A scrape begins with a Crawl seed.** Make the start URL(s) + page mode a value
you must hold before any builder exists, and the schema the only thing you can
do with that value. "Build with no start URLs or no schema" then has no
representation to construct.

- **Crawl seed** (new `CONTEXT.md` term — proposed; the maintainer drives
  `CONTEXT.md`): the start URL(s) and page mode, awaiting a `Schema`; not yet a
  builder. Produced by static entry methods:
  - `Crawl(params string[] urls)` → a Static-mode seed.
  - `CrawlWithBrowser(string[] urls, List<PageAction>? actions = null)` → a
    Dynamic-mode seed.
- The seed exposes exactly **one** member: `Extract(Schema schema)`. The only
  thing you can do with a seed is give it a schema. This is the single new
  public interface, **`ICrawlSeed`** — named for the `CONTEXT.md` concept it
  *is* (a Crawl seed), so a reader who finds the term finds the type. The
  grill's earlier `IExtractStep` named the fluent stage — the mechanism;
  `ICrawlSeed` names the domain noun, the same intent-over-mechanism move that
  took the schema verb from `Parse` to `Extract`.
- `seed.Extract(schema)` returns the **one** free builder — every existing free
  fluent method (`Follow`, `Paginate`, `WithLogger`, `WriteToJsonFile`, …) and
  **every ADR-0009 satellite extension** (`TrackVisitedLinksInRedis`,
  `WriteToMongoDb`, `WithPuppeteerPageLoader`, …) unchanged, because the
  receiver type is unchanged. Zero satellite ripple.
- The free builder carries the **gated** terminals — both reachable only after
  `Crawl(…).Extract(…)`, so both unset-config failures are unrepresentable:
  - `BuildAsync()` → `ScraperEngine` (in-process + worker-service).
  - `Build()` → `ScraperConfig` (the distributed *start endpoint*; replaces the
    standalone `new ConfigBuilder()….Build()` in `StartScraping.cs`).
- The ADR-0009 reduced shell moves to its own public type,
  **`DistributedSpiderBuilder`** (`new DistributedSpiderBuilder()
  .WithLogger(…).BuildSpider()`). It is config-agnostic by design — no Crawl
  seed, no `BuildAsync`, no crawl-shape — and carries only the spider-side
  runtime seams a worker actually configures. The driver-side link tracker is
  *not* on it (ADR-0022: it is the driver's idempotency authority, wired by the
  worker directly — see *Implementation note*). This is why `ScraperEngineBuilder`
  can have an internal constructor: the seedless path no longer needs it.
- The two `ConfigBuilder.Build()` `throw`s and `CLAUDE.md` gotcha #3 are
  **deleted outright** — there is nothing left for them to catch or warn about.
  `ConfigBuilder` is now `internal`; `ScraperEngineBuilder`'s constructor is
  `internal` (test-only via `InternalsVisibleTo`).

This is **R-complete** (the resolved fork): a single Crawl seed fronts both
build outcomes. The alternative — a second, parallel staged front bolted onto a
still-public `ConfigBuilder` — was rejected as reintroducing the very
duplication-with-drift the ADR exists to remove (see *Considered options*).

Client code, before and after:

```csharp
// in-process — before
await new ScraperEngineBuilder()
    .Get("https://www.alexpavlov.dev/blog")
    .Follow(".text-gray-900.transition")
    .Parse(new() { new("title", ".text-3xl.font-bold"),
                   new("text",  ".max-w-max.prose") })
    .WriteToJsonFile("output.json").PageCrawlLimit(10).BuildAsync();

// in-process — after: the prefix is mandatory, the type enforces it
await ScraperEngineBuilder
    .Crawl("https://www.alexpavlov.dev/blog")
    .Extract(new() { new("title", ".text-3xl.font-bold"),
                     new("text",  ".max-w-max.prose") })
    .Follow(".text-gray-900.transition")
    .WriteToJsonFile("output.json").PageCrawlLimit(10).BuildAsync();

// distributed start endpoint — after: same seed, config terminal
var config = ScraperEngineBuilder
    .Crawl("https://rutracker.org/forum/index.php?c=33")
    .Extract(schema)
    .Follow("#cf-33 .forumlink>a").Paginate("a.torTopic", ".pg")
    .Build();                       // ScraperConfig, then persist
await _configStorage.CreateConfigAsync(config);

// distributed worker — its own seam (ADR-0009 reduced shell)
var spider = new DistributedSpiderBuilder()
    .WithLogger(log).BuildSpider();   // link tracker is the driver's (ADR-0022)
```

`ICrawlSeed`, the static `Crawl`/`CrawlWithBrowser`, and
`DistributedSpiderBuilder` are the public surface added, so they carry the
ADR-0023 documented-contract obligation (XML doc, CS1591-as-error) — one tiny
interface plus a small reduced-shell builder, fully the new doc burden.

## Implementation note: the trilemma

Implementing the Decision surfaced a constraint the prose had glossed. Three
properties the ADR promised cannot all hold with one builder type and no
generics:

1. **Structural guarantee** — `BuildAsync()`/`Build()` unreachable without
   `Crawl().Extract()` ⇒ the type carrying them has no public constructor.
2. **Seedless `BuildSpider()`** — the ADR-0009 worker constructs its shell with
   no seed.
3. **Zero satellite ripple** — satellite extensions stay
   `this ScraperEngineBuilder`, so the post-`Extract` type *is*
   `ScraperEngineBuilder`.

(1) and (2) collide directly on the same type. The resolution, confirmed with
the maintainer on "best architecture + most beautiful API": embrace the ADR's
own "two seams, not one bug" framing as *two types*. `ScraperEngineBuilder`
(internal ctor, reached only via the Crawl seed) carries the gated terminals
and every satellite extension, unchanged. The reduced shell is a separate
public type, `DistributedSpiderBuilder`, with a public constructor and no
`BuildAsync` — so the guarantee is absolute *and* the worker is still seedless.

The load-bearing fact that made this cost-free: the canonical distributed
worker ([`AzureFuncs/Startup.cs`](../../Examples/WebReaper.AzureFuncs/Startup.cs))
wires its shared adapters by **direct construction** (`new
RedisVisitedLinkTracker(...)`, `new RedisOutstandingWorkLatch(...)`) — satellite
concretes are public precisely for this DIY pattern (ADR-0009). The worker
never needed the satellite *builder* sugar, so keeping satellite extensions on
`ScraperEngineBuilder` only is **zero ripple**, not a regression. Test
compilation is likewise unaffected: `WebReaper.csproj`'s `InternalsVisibleTo`
already covers every test assembly, so the internal constructor stays visible
to white-box tests while the *public* contract has no seedless builder. The
worker's previously-wired `WithLinkTracker` was dropped: per ADR-0022 the
visited-link tracker is the *driver's* idempotency authority, used by the
worker function directly, never a spider-shell concern.

## Considered options

- **Type-state phases — phantom `<TPhase>` threaded through the builder
  (rejected).** Encodes the same ordering in the type, but `ScraperEngineBuilder`
  has ~20 fluent methods and is the receiver of **14 `this ScraperEngineBuilder`
  satellite extension methods** across Mongo, Sqlite, Redis, Cosmos,
  AzureServiceBus and Puppeteer. Every one would have to become generic in the
  phase — re-coupling the ADR-0009 registration seam this codebase worked to
  decouple, a heavy ADR-0023 public-surface churn, and a phantom generic that
  leaks into client-visible types and compiler errors. It fails the maintainer's
  first principle: easy to read. The Crawl seed achieves the same impossibility
  with one interface and one method, and no satellite ripple.
- **Make `BuildSpider()` symmetric + sharpen the throw (rejected).** The minimal
  non-structural fix. Keeps the failure at runtime, keeps the `CLAUDE.md`
  gotcha; fails the deletion test. Worse, "symmetric `BuildSpider()`" is itself
  wrong: `BuildSpider()` is the seedless ADR-0009 seam — making it validate
  in-builder config would break the distributed worker (*The verification that
  reshaped the decision*). The signature move is unrepresentable, not
  better-documented.
- **Multi-parameter factory, e.g. `ScraperEngineBuilder.Create(urls, schema,
  …)` (rejected).** Enforces the prerequisites by making them constructor
  arguments, but a factory taking several positional parameters is exactly the
  unreadable call site the maintainer named as the thing to avoid; it discards
  the self-describing fluent surface.
- **R-narrow: stage only `ScraperEngineBuilder` (rejected in grilling).** Leaves
  `ConfigBuilder`'s standalone public use (`StartScraping.cs`, the DIY-distributed
  start endpoint) still runtime-throwing; the headline "unrepresentable by
  construction" would be only half-true and the `CLAUDE.md` gotcha would
  survive, scoped instead of removed. ADR-0024's lesson applies: process
  discipline (or a scoped doc) is not a fix for a missing structural guarantee.
- **Status quo + the `CLAUDE.md` gotcha (rejected).** A documented gotcha is the
  smell this ADR removes, not the fix for it.

## SemVer / impact

**Breaking public API change — a major bump (next after 9.0.0 ⇒ 10.0.0).** A
clean-cut break, called out loud, never silent (the ADR-0009 / ADR-0023
precedent for deliberate majors):

- Entry moves: `new ScraperEngineBuilder().Get(...)/.GetWithBrowser(...)` and
  `.Parse(...)`/`.WithScheme(...)` are replaced by the static
  `Crawl(...)`/`CrawlWithBrowser(...)` + `Extract(...)` seed.
- The two `ConfigBuilder.Build()` `throw`s are deleted; `CLAUDE.md` gotcha #3 is
  deleted.
- **Public `ConfigBuilder` is retired** — its sole external use is the
  distributed-start `new ConfigBuilder()….Build()`, which the unified Crawl seed
  absorbs as the `.Build()` terminal; `ConfigBuilder` becomes an internal
  implementation detail of the façade (the ADR-0023 "concrete is internal"
  direction). This is the one consequence flagged for the maintainer to confirm
  at implementation rather than silently take.
- The seedless ADR-0009 distributed-worker path **moves** from
  `new ScraperEngineBuilder()….BuildSpider()` to
  `new DistributedSpiderBuilder()….BuildSpider()` (a breaking move; the worker
  also drops the driver-owned `WithLinkTracker`, ADR-0022). `BuildSpider()` is
  removed from `ScraperEngineBuilder`.
- README, every `Examples/` project, and `CLAUDE.md` updated in the same wave;
  satellite READMEs / `API.md` / `CHANGELOG` follow.
- Guardrail (met): whole-solution build **0 errors**, **94 unit + 27 satellite**
  tests green, **Native-AOT smoke ALL PASS**. The four `throw`/gotcha sites are
  gone and the only public way to reach `BuildAsync()`/`Build()` is
  `Crawl(…).Extract(…)`.

## References

- ADR-0001 (closed `CrawlOutcome` sum) and ADR-0022 (termination as a value,
  never a thrown exception) — the project's signature move, here extended to the
  builder front door it most affects.
- ADR-0009 (registration seam, satellite adapters) — why `BuildSpider()` stays
  seedless and why the phantom-generic alternative's satellite ripple is the
  load-bearing rejection.
- ADR-0023 (the public surface *is* the documented contract, CS1591-as-error) —
  the doc obligation the one new interface carries, and the churn cost that
  sinks the phantom-generic alternative.
- ADR-0024 — the "Proposed — design pass" status and the explicit-grilling-fork
  precedent this ADR follows.
- [`ConfigBuilder.cs:208-215`](../../WebReaper/Builders/ConfigBuilder.cs#L208-L215),
  [`ScraperEngineBuilder.cs:462-488`](../../WebReaper/Builders/ScraperEngineBuilder.cs#L462-L488),
  `Examples/WebReaper.AzureFuncs/StartScraping.cs` &
  `WebReaperSpider.cs` — the verified friction and the seam this preserves.
- The `/improve-codebase-architecture` review (candidates #1–#4; #1–#3 shipped
  as ADR-0022) — the origin of this candidate.
