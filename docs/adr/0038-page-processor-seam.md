# The post-extraction surface becomes two seams: a page-processor pipeline and the Sink

## Status

**Accepted — implementation complete** (2026-05-23; landed on branch
`adr-0038-page-processor-seam` off `origin/master`). Candidate #2 of the
maintainer's final `/improve-codebase-architecture` pass before the AI-native
work — candidate #1 was ADR-0037. Breaking: `ScraperEngineBuilder.PostProcess`
and the `Metadata` Tier-1 `Domain` type are removed; `Subscribe` is re-shaped.
Folds into the unreleased 10.0.0 wave.

## Context

When the **Crawl driver** has a crawled **Target page**'s `ParsedData`,
`ScraperEngine.ProcessTargetPage` fans it to **three** differently-shaped
places, in order:

| | Shape | Sees | Registered via | Multiplicity |
|---|---|---|---|---|
| `PostProcessor` | `Func<Metadata, JsonObject, Task>` | record + **raw HTML** + ancestry | `PostProcess` | single-assignment (`=`) |
| `ScrapedData` | `Action<ParsedData>` | the record | `Subscribe` | multicast (`+=`) |
| `IScraperSink` | `Task EmitAsync(ParsedData, ct)` | a deep-cloned record | `AddSink` | a list, concurrent fan-out |

`IScraperSink` is the named **Sink** — documented, on the **Registration
seam**, **Adapter warm-up**-capable. The other two are anonymous delegate
parameters on the `ScraperEngine` constructor, named nowhere in CONTEXT.md. A
consumer reacting to an extracted page meets three doors and tells them apart
by reverse-engineering signatures.

Measured against
[LANGUAGE.md](../../.claude/skills/improve-codebase-architecture/LANGUAGE.md):

- **`Subscribe` / `ScrapedData` is a shallow seam shadowing the deep one.** Its
  capability — observe a `ParsedData` — is a strict subset of a **Sink**'s; a
  **Sink** does that plus async, cancellation, **Adapter warm-up**, and a
  parallel-safe clone. The **deletion test**: delete `Subscribe`, and a caller
  writes a 3-line **Sink** — complexity barely moves. It also *leaks* ADR-0031:
  every **Sink** gets its own deep clone *because the record is mutable and the
  fan-out concurrent*; `ScrapedData` was handed the **shared** record.
- **`PostProcess` / `PostProcessor` earns its keep but is mis-shaped.** It is
  the *only* post-extraction hook that sees the raw page and the crawl
  ancestry, and it runs before the **Sink**s so its enrichment flows
  downstream — a real, distinct capability. But it is an anonymous `Func`,
  **single-assignment** (a second `PostProcess` silently drops the first), and
  its `Metadata` doc claimed the callback "can enrich or filter" while
  `Func<…, Task>` returns nothing — it could enrich, never filter. The type
  could not do what the doc promised.

The friction has one root: **the post-extraction surface was never given a
shape.** Three mechanisms accreted; nobody asked what the *roles* are.

This is also the surface the upcoming AI work lands on — extraction
validation, confidence-scoring, selector-repair-on-drift, page classification
are all "react to an extracted page," and the strong ones need the raw page to
re-extract. They would each face the incoherent trio.

## Decision

**Two roles, two seams.** There are exactly two things one does with an
extracted **Target page**:

- **Emit** — send the final record to a destination. Terminal, concurrent
  fan-out, N destinations, often out-of-process. = the **Sink**, unchanged.
- **Process** — run logic over the extracted page *before* emit: enrich,
  observe, filter, and — the AI cases — validate, score, repair. Ordered,
  in-pipeline, sees the raw page. = a new **page-processor** seam.

Not *one* seam (everything a **Sink**): `PostProcess` needs the raw HTML, and
forcing that onto `IScraperSink.EmitAsync` bloats what flows to every
Redis/Cosmos/file **Sink**; and **Sink**s fan out concurrently off clones
while processing is an ordered pipeline — different concurrency models.

### 1. `Subscribe` folds into the Sink seam

`ScraperEngineBuilder.Subscribe(Action<ParsedData>)` keeps its signature but is
now sugar: it registers a `DelegateSink` — a Tier-2 internal `IScraperSink`
wrapping the delegate — through `AddSink`. The `ScrapedData` delegate field,
`SpiderBuilder.AddSubscription`, and the driver's separate `ScrapedData?.Invoke`
are gone. A `Subscribe` handler now composes with the other **Sink**s, receives
its own clone (the ADR-0031 leak closed), and is async / warm-up-capable for
free. The shallow parallel seam is removed; the one-line convenience survives.

### 2. The page-processor seam (`IPageProcessor`)

A new `WebReaper.Processing` namespace. `IPageProcessor` is one method:

```csharp
public interface IPageProcessor
{
    ValueTask<PageVerdict> ProcessAsync(PageContext context, CancellationToken cancellationToken);
}

public sealed record PageContext(
    ParsedData Data,                  // processor N sees processor N-1's record
    string Html,                      // the raw page — the LLM re-extraction input
    IReadOnlyList<string> BackLinks,  // crawl ancestry
    Schema? Schema);                  // null for a link-only crawl

public abstract record PageVerdict           // closed sum, ADR-0001 lineage
{
    private PageVerdict() { }
    public sealed record Kept(ParsedData Data) : PageVerdict;     // enrich / observe / repair
    public sealed record Dropped(string Reason) : PageVerdict;    // filter — no Sink emits it
    public static PageVerdict Keep(ParsedData data) => new Kept(data);
    public static PageVerdict Drop(string reason) => new Dropped(reason);
}
```

The **Crawl driver** runs every registered processor over each **Target page**,
in registration order, *before* the **Sink** fan-out — processor N is handed
processor N-1's record (an ordered pipeline; **Sink**s, by contrast, fan out
concurrently). A processor **enriches** (mutate `context.Data.Data`, `Keep`),
**observes** (`Keep` unchanged), **replaces / repairs** (`Keep` a different
record — the AI re-extraction case), or **filters** (`Drop` — the pipeline
stops, no **Sink** emits the page). `PostProcess` / `PostProcessor` /
`Metadata` are removed.

Six design decisions, from a "design it three ways" exploration (see Considered
options):

(a) **`PageVerdict` is a two-arm closed sum** — `Kept` | `Dropped`. Enrich,
observe and repair are all `Kept` (the record continues, mutated / unchanged /
replaced); `Dropped` is filter. No third "short-circuit" arm — skipping a
downstream processor is *that* processor's own decision, not an earlier one
reaching forward to control the pipeline.

(b) **`ValueTask`** — the common processor is a synchronous enrich; a completed
`ValueTask` allocates nothing. Matches `ICrawlStep.StepAsync`'s
`ValueTask<CrawlOutcome>`, the per-page hot path.

(c) **`PageContext` carries the parsing `Schema?`** — nullable: a link-only
crawl has no schema (`ScraperConfig.ParsingScheme` is itself `Schema?`). A
selector-repair processor needs to see what the deterministic fold was told to
extract.

(d) **The pipeline threads `ParsedData`, not a bare `JsonObject`** — the
ADR-0031 "the URL is folded into `Data`" invariant holds end-to-end, no
post-pipeline URL re-merge. The trivial-enrich `Process(Action<JsonObject>)`
overload still hands the consumer the `JsonObject` directly (the adapter
reaches `.Data.Data`).

(e) **Three `Process` registration overloads** — `Process(IPageProcessor)`
(the seam, for a stateful processor), `Process(Func<PageContext,
CancellationToken, ValueTask<PageVerdict>>)` (a stateless delegate),
`Process(Action<JsonObject>)` (the dead-common one-liner enrich, no
`PageVerdict` to learn). Not a wide sugar surface; more can be added later,
non-breakingly.

(f) **A processor that throws drops that page** — the driver wraps each
`ProcessAsync` in try/catch; `OperationCanceledException` propagates
(cancellation is cooperative), any other exception is logged and the page is
dropped, the crawl continuing. A noisy page never aborts the crawl (the
ADR-0029 posture). Retry, if a processor wants it, lives inside the processor
around its own I/O.

### 3. Warm-up reuses `IAsyncInitializable`

A processor that holds a durable resource (an LLM `IChatClient`) also
implements `IAsyncInitializable` (ADR-0033); the **Crawl driver** warms it once
before the crawl loop, with the schedulers, trackers and **Sink**s. The seam
itself stays one method — warm-up composes without touching it.

### Bounded scope — what this does NOT change

- `IScraperSink` — untouched.
- The `ParsedData` record and ADR-0031's URL-merge — untouched; the pipeline
  threads `ParsedData` precisely to keep that invariant.
- **No built-in page-processor adapter ships in core.** The seam is a consumer
  extension point; a ready-made LLM processor belongs in a future
  `WebReaper.AI` satellite, keeping `Microsoft.Extensions.AI` off the core
  graph (ADR-0009's dependency-light principle). An empty pipeline is a
  no-op — the driver fans straight to the **Sink**s.
- The distributed driver stays consumer-authored (ADR-0009) — it wires its own
  processors.

## Considered options

The page-processor *interface* was designed three ways in parallel ("Design It
Twice", Ousterhout); the chosen shape is a hybrid.

### (A) Minimal — one method, maximum leverage

One-method `IPageProcessor` returning a closed `PageVerdict`. **Adopted as the
base** — enrich / observe / filter / repair, plus every AI case, behind one
method and one two-arm sum.

### (B) Maximally flexible — middleware, short-circuit

Adds a 3rd `ShortCircuit` verdict arm, an implicit `ParsedData → Kept`
conversion, and frames composition as "a processor that holds a processor."
**Partly adopted:** the composition insight is taken — a middleware processor
needs no `next` parameter; the ASP.NET-style `next` is rejected as the "forgot
to call `next`, the pipeline silently truncates" footgun. `ShortCircuit` is
rejected — see decision (a). The implicit conversion is rejected — explicit
`PageVerdict.Keep(...)` is clearer for a two-arm sum.

### (C) Optimised for the common caller — rich sugar

A 7-method `Process` / `Where` surface; `ProcessAsync` taking a bare
`JsonObject`. **Partly adopted:** the trivial-enrich `Process(Action<JsonObject>)`
one-liner is taken. The bare-`JsonObject` interface is rejected — it
re-introduces a post-pipeline `"url"` re-merge that ADR-0031 consolidated into
`ParsedData`'s constructor. The 7-method surface is rejected — that trades one
shallow seam for a wide shallow builder; three overloads suffice.

### (D) One seam — everything is a Sink

Rejected: `PostProcess` needs the raw HTML; forcing it onto
`IScraperSink.EmitAsync` bloats what flows to every database / file **Sink**,
and the concurrency models differ (ordered pipeline vs concurrent fan-out).

### (E) Keep three seams, only name and document them

Rejected: naming `Subscribe` in CONTEXT.md does not make it deep — it is a
shallow shadow of `IScraperSink` whatever it is called. This review exists to
remove shallow modules, not to document them.

### (F) A processor exception enters the per-Job retry policy

Rejected: the page-processor pipeline runs in `ProcessTargetPage`, *outside*
the `IRetryPolicy`-wrapped Spider call (ADR-0026); and a flaky LLM endpoint
should not re-load and re-crawl the whole page. A processor throw drops the
page (decision (f)); retry is the processor's own concern, around its own I/O.

## Consequences

- **The post-extraction surface has a shape: two seams, two roles** — Emit (the
  **Sink**) and Process (the page-processor pipeline). The incoherent trio is
  gone.
- **A shallow seam is removed.** `Subscribe` / `ScrapedData` is no longer a
  parallel mechanism — it is `DelegateSink` sugar over the **Sink** seam, and
  it inherits the clone, the async contract and **Adapter warm-up** the seam
  already has (the ADR-0031 shared-record leak is closed in passing).
- **The AI hooks have a home.** Validation, confidence-scoring, selector-repair
  and classification are all `IPageProcessor`s — content + page in, a verdict
  out, with the raw HTML and the `Schema` to hand. The seam was designed
  against those four cases.
- **`PostProcess` mis-shapes are fixed by construction** — single-assignment
  becomes an ordered list (`AddProcessor`); "enrich or filter" is now true
  (`Drop` is a real verdict arm).
- **Breaking** — `ScraperEngineBuilder.PostProcess` and the public `Metadata`
  `Domain` type are removed; a consumer moves a `PostProcess` callback to
  `.Process(...)`. `Subscribe`'s signature is unchanged (call sites compile)
  but it is now a **Sink**. The `ScraperEngine` constructor (internal) drops
  two callback parameters and gains the processor list. SemVer **major** —
  folds into the unreleased 10.0.0 wave (with ADR-0032–0037).
- **New public Tier-1 surface** — `IPageProcessor`, `PageContext`, `PageVerdict`
  (`WebReaper.Processing`), documented to the ADR-0023 bar; `DelegateSink` and
  `DelegatePageProcessor` are Tier-2 internal.
- **CONTEXT.md** gains **Page processor** and **Page verdict** as defined
  terms, the **Sink** entry notes `Subscribe`, and a new "Flagged ambiguities"
  bullet records the decision; **CLAUDE.md**'s run-path paragraph is corrected.

## Implementation

Landed on `adr-0038-page-processor-seam`:

1. **New `WebReaper/Processing/`** — `Abstract/IPageProcessor.cs`,
   `PageContext.cs`, `PageVerdict.cs` (Tier-1 public, documented), and
   `Concrete/DelegatePageProcessor.cs` (Tier-2 internal — backs the delegate
   `Process` overloads).
2. **`WebReaper/Sinks/Concrete/DelegateSink.cs`** (new, Tier-2 internal) —
   wraps an `Action<ParsedData>` as an `IScraperSink`; backs `Subscribe`.
3. **`WebReaper/Builders/SpiderBuilder.cs`** — `PostProcessor` / `ScrapedData`
   fields and `PostProcess` / `AddSubscription` removed; `PageProcessors` list
   + `AddProcessor` + `DriverPageProcessors` added.
4. **`WebReaper/Builders/ScraperEngineBuilder.cs`** — `Subscribe` reroutes to
   `AddSink(new DelegateSink(...))`; `PostProcess` removed; three `Process`
   overloads added; `BuildAsync` passes `DriverPageProcessors` to the engine.
5. **`WebReaper/Core/ScraperEngine.cs`** — the constructor drops `scrapedData`
   / `postProcessor`, gains `IReadOnlyList<IPageProcessor>? pageProcessors`;
   `ProcessTargetPage` runs the ordered pipeline (try/catch per processor,
   `OperationCanceledException` rethrown, drop-on-throw and drop-on-`Dropped`)
   before the **Sink** fan-out; `WarmUpAdaptersAsync` warms processors too.
6. **`WebReaper/Domain/Metadata.cs`** — deleted; doc references in
   `JobReport.cs`, `Job.cs`, `ISpider.cs`, `Spider.cs` updated.
7. **`Examples/WebReaper.ScraperWorkerService/ScrapingWorker.cs`** — its
   `PostProcess(ParseTorrentStats)` callback becomes `.Process(...)` over an
   `IPageProcessor`-shaped delegate.
8. **Tests** — `ScraperEngineDriverTests`' callback test is reworked to the
   pipeline; four cases added (ordered pipeline, `Drop` filters, a throwing
   processor drops only its page, `Subscribe`-as-`DelegateSink`).
9. **`CONTEXT.md`** — **Page processor** / **Page verdict** terms added, the
   **Sink** entry updated, one new "Flagged ambiguities" bullet. **`CLAUDE.md`**
   run-path paragraph corrected.

### Guardrails (run on the branch tip)

- `dotnet build WebReaper.sln` — **0 errors**, 18 warnings, all pre-existing
  (CS8618 / CS8633 / CS8602 / CS8604 / CS1574 / NU1510) — none in a file this
  ADR touches; `WarningsAsErrors=CS1591` on core stays green — the three new
  public types and every member carry XML docs.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — **167/167 pass** (163
  pre-0038 + 4 new page-processor cases; the old sink-and-callbacks case
  reworked to the pipeline).
- Native-AOT smoke (`WebReaper.AotSmokeTest`, `dotnet publish -r osx-arm64`) —
  **ALL PASS** (9/9). The seam adds no reflection, serialization, or dynamic
  codegen — `IPageProcessor` / `PageContext` / `PageVerdict` are a plain
  interface and two records.
- The live-site `WebReaper.IntegrationTests` are deferred to CI (slow,
  network-flaky by design — CLAUDE.md).

## References

- **ADR-0022** — the Crawl driver owns the Sink fan-out and the callbacks; this
  ADR gives that surface a shape.
- **ADR-0031** — `ParsedData`'s URL-merge invariant; the pipeline threads
  `ParsedData` to preserve it, and `Subscribe`-as-Sink closes the
  shared-record leak.
- **ADR-0033** — `IAsyncInitializable`; a page processor reuses it for
  one-time async warm-up.
- **ADR-0029** — a noisy page never aborts the crawl; a processor that throws
  drops only its page.
- **ADR-0009** — the Registration seam and the satellite pattern; a future
  `WebReaper.AI` satellite hosts ready-made LLM processors.
- **ADR-0001** — the closed-sum posture `PageVerdict` follows.
- **ADR-0023** — the Tier-1 / Tier-2 split: the seam types are Tier-1 public,
  the delegate adapters Tier-2 internal.
