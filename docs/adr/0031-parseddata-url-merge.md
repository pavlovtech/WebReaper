# ParsedData's construction owns the URL-merge; the sink fan-out hands each sink its own copy

## Status

**Accepted — implementation complete** (2026-05-22; landed on branch
`adr-0031-parseddata-url-merge` off `origin/master` 78190bb — sixth
ADR of the fresh `/improve-codebase-architecture` review wave, after
ADR-0026 through ADR-0030 (PRs #82-86, all merged). It is candidate #5
of the 2026-05-21 review — the runner-up flagged when candidate #3 was
picked for ADR-0030.) Behaviourally-additive with one narrow edge
(constructing a `ParsedData` now mutates the passed `JsonObject` to
fold in the page URL; `ParsedData`'s public shape and the `CrawlStep`
construction call are unchanged). Folds into the 10.0.0 wave the user
is batching.

## Context

**ParsedData** — CONTEXT.md: *"the extracted record from one target
page — its URL plus a JSON object"* — is the record

```csharp
public record ParsedData(string Url, JsonObject Data);
```

— [ParsedData.cs](../../WebReaper/Sinks/Models/ParsedData.cs). It is
born at exactly one non-test site,
[CrawlStep.cs:39](../../WebReaper/Core/Crawling/Concrete/CrawlStep.cs#L39):
`CrawlOutcome.Target(new ParsedData(job.Url, data))`, where `data` is
the **Schema fold**'s freshly-produced `JsonObject` — the extracted
fields only. The page URL rides on the *envelope*, never in the JSON.

Every persisting **Sink** needs the URL *inside* the record — a row in
the file, a Mongo/Cosmos document, a Redis set member is useless
without knowing which page it came from. So each one folds the URL in
itself, as the first line of `EmitAsync`:

- [BufferedFileSink.cs:60](../../WebReaper/Sinks/Concrete/BufferedFileSink.cs#L60) — `entity.Data["url"] = entity.Url;`
- [CosmosSink.cs:46](../../WebReaper.Cosmos/CosmosSink.cs#L46) — same line
- [MongoDbSink.cs:48](../../WebReaper.Mongo/MongoDbSink.cs#L48) — same line
- [RedisSink.cs:31](../../WebReaper.Redis/RedisSink.cs#L31) — same line
- [ConsoleSink.cs:12](../../WebReaper/Sinks/Concrete/ConsoleSink.cs#L12) — **does not.** It prints `entity.Data` only; the URL is silently absent from console output.

Two distinct frictions, measured against
[LANGUAGE.md](../../.claude/skills/improve-codebase-architecture/LANGUAGE.md):

**1 — Shallow duplication.** The line `entity.Data["url"] = entity.Url`
is copy-pasted across four sinks. The **deletion test**: remove all
four — the URL vanishes from every persisted record. The merge is
load-bearing; it lives in four copies; **`ConsoleSink` is the copy that
drifted** — exactly the ADR-0002 / ADR-0003 / ADR-0006 *"copies drifted
into a bug that exists in only one copy"* shape, here in the
non-file-sink cluster ADR-0006 did not reach. The knowledge *"the
emitted record is the fold output with the page URL folded in"* has no
home — `ParsedData` carries the URL and the data side by side but does
not own the relationship between them; every sink re-derives it.
Locality is missing.

**2 — A race on a shared mutable `JsonObject`.** The **Crawl driver**
fans every target page out to all sinks at
[ScraperEngine.cs:238-241](../../WebReaper/Core/ScraperEngine.cs#L238-L241):
`Sinks.Select(sink => sink.EmitAsync(result, …))` then `Task.WhenAll`.
Every sink receives *the same* `ParsedData` — the same `JsonObject`
instance — and runs concurrently. `JsonObject` is not thread-safe; the
four merging sinks each do a structural mutation (`Data["url"] = …`) on
one shared dictionary at once. `CosmosSink` additionally writes
`entity.Data["id"] = Guid.NewGuid()` — a *different* value
([CosmosSink.cs:44-46](../../WebReaper.Cosmos/CosmosSink.cs#L44-L46)).
Even the four `url` writes (same value) can corrupt the dictionary's
internal state under concurrent structural mutation.

The two frictions share one root: **`ParsedData` is a shallow
two-field pair.** It does not own its own completeness, so the
completing step (the merge) leaks into every sink; and because the
sinks all mutate one shared instance, that leak is also a race.

## Decision

Deepen `ParsedData` so its *construction* produces the canonical
emitted record, and hand each sink its own copy at the fan-out. Four
changes:

1. **`ParsedData`'s construction owns the URL-merge.** Via the
   property-initializer trick ADR-0030 used on `LinkPathSelector` — the
   idiomatic positional-record validation/normalisation point — the
   `Data` property initializer folds `Url` into the `JsonObject`:

   ```csharp
   public record ParsedData(string Url, JsonObject Data)
   {
       /// <summary>The extracted fields, with the page URL folded in
       /// under "url" — the canonical emitted record (ADR-0031).</summary>
       public JsonObject Data { get; init; } = MergeUrl(Url, Data);

       private static JsonObject MergeUrl(string url, JsonObject data)
       {
           data["url"] = url;   // one home for the merge; idempotent
           return data;
       }
   }
   ```

   `ParsedData`'s public positional shape `(string Url, JsonObject Data)`
   is **unchanged**; [CrawlStep.cs:39](../../WebReaper/Core/Crawling/Concrete/CrawlStep.cs#L39)
   — `new ParsedData(job.Url, data)` — is **unchanged**. Every
   `ParsedData` ever constructed now structurally carries `url` in
   `Data`. `Url` stays as the typed accessor — set-once, immutable on
   the record, a typed *view* of the same datum, not redundant *mutable*
   state. The merge is idempotent.

2. **The four sinks drop their merge line.** `BufferedFileSink`,
   `CosmosSink`, `MongoDbSink`, `RedisSink` delete
   `entity.Data["url"] = entity.Url;`. `CosmosSink` keeps its
   `entity.Data["id"] = …` write (the Cosmos `/id` partition key — a
   real Cosmos quirk, not the shared duplication). `ConsoleSink` is
   unchanged in code but now prints the URL for free — it prints
   `entity.Data`, which now carries `url`.

3. **The sink fan-out hands each sink its own copy.**
   `ScraperEngine.ProcessTargetPage` clones `Data` per sink:

   ```csharp
   var sinkTasks = Sinks.Select(sink =>
       sink.EmitAsync(result with { Data = (JsonObject)result.Data.DeepClone() },
           cancellationToken));
   ```

   The shared-`JsonObject` race is structurally gone — each sink owns
   its `JsonObject`; `CosmosSink`'s `["id"]` write lands on its own
   clone. The `with` expression uses the synthesised copy constructor,
   which does **not** re-run the `Data` property initializer, so there
   is no double-merge — the clone is a `DeepClone` of the
   already-merged `Data`. The callbacks (`PostProcessor`, `ScrapedData`)
   run *sequentially before* the fan-out and need no clone; only the
   parallel fan-out does. `PostProcessor`'s mutation is still seen by
   `ScrapedData` and by every sink's clone (each clone is taken after
   `PostProcessor` has run).

4. **`IScraperSink.EmitAsync` documents the guarantee.** Its summary
   gains: *the sink receives its own `ParsedData`; the page URL is
   already present in `Data` under `"url"`; the sink may mutate `Data`
   freely.* A custom sink no longer needs to merge the URL — and one
   that still does is harmless (idempotent).

One edge, named: **`"url"` is a reserved key in the emitted record.** A
Schema field literally named `url` is overwritten by the page URL. This
is unchanged behaviour — the four sinks already clobbered it — now
documented rather than emergent.

This is behaviourally-additive: `ParsedData`'s public shape is
unchanged, `CrawlStep` is unchanged, and the persisted output of the
four built-in persisting sinks is byte-identical (they merged `url`
anyway). The observable changes are all improvements — Console output
gains the URL; the race is gone. The one narrow edge: constructing a
`ParsedData` now mutates the passed `JsonObject`.

## Considered options

### (a) Merge in `CrawlStep`, keep `ParsedData` a dumb pair

`CrawlStep.cs:39` does `data["url"] = job.Url;` before
`new ParsedData(...)`. **Rejected:** the invariant *"Data contains
url"* is then enforced by convention — `CrawlStep` *happens* to do it.
A second construction site (a test, a future distributed-worker code
path) would silently produce a `ParsedData` with no `url`. The type
should own its own invariant — the ADR-0028 / ADR-0030 pattern, applied
a third time. `ParsedData`'s constructor is the one site every path
goes through.

### (b) Collapse `ParsedData` to `JsonObject`

Remove the record; the sink seam and `CrawlOutcome.Parsed` carry a bare
`JsonObject`. **Rejected:** a one-field wrapper *would* be shallow — but
the deepened `ParsedData` is not one-field: its construction does real
work (the merge) and it is the home of the *"carries url"* invariant.
Collapsing throws away both the typed `Url` accessor (used by
`Subscribe` consumers, and readable in signatures) and the invariant's
home. It is also needlessly breaking — `ParsedData` is a documented
Tier-1 type (ADR-0023).

### (c) Keep the merge per-sink, extract a shared helper

A `MergeUrl` extension method every sink calls. **Rejected:** still a
per-sink *call* — every sink must remember to invoke it, and
`ConsoleSink` would still be free to forget. That is a hypothetical
seam, not a real one (LANGUAGE.md: *"one adapter = hypothetical"*). The
merge is not a sink concern at all; it is the record's completeness.

### (d) Don't clone; forbid sinks from mutating `Data`

Document *"sinks must not mutate `Data`"* and make `CosmosSink` clone
internally for its `id`. **Rejected:** a contract rule a sink can
violate silently is not enforcement — the same reasoning ADR-0030's
option (g) rejected for documentation-not-enforcement. A custom-sink
author will not know. Cloning once at the fan-out makes the safety
structural and frees *every* sink — built-in and custom — to mutate.

### (e) Make `ParsedData.Data` immutable / read-only

A read-only view no sink can mutate. **Rejected:** `CosmosSink`
legitimately enriches the record (`id`); a future sink may too. An
immutable `Data` forces every such sink to clone-then-mutate by hand —
pushing the clone into N sinks instead of one fan-out. Clone-once-per-
sink at the driver is the single home.

### (f) Make `Url` a computed property over `Data["url"]`

`public string Url => (string)Data["url"]!;` — no stored duplicate,
single source of truth. **Considered, marginal.** It does a JSON lookup
and a null-forgiving cast on every read; the stored set-once `Url` on an
immutable record cannot drift from `Data["url"]`, so the "duplication"
is a benign typed view. Chose stored `Url` — simpler, and the positional
record shape stays untouched.

## Consequences

- **The URL-merge has one home** — `ParsedData`'s construction. The four
  sink copies and the `ConsoleSink` drift are gone.
- **`ParsedData` deepens** — from a shallow two-field pair to a record
  whose construction produces the canonical emitted record. Small
  interface (URL + fold output), real behaviour and a real invariant
  behind it.
- **Console output gains the URL** — a latent bug fixed for free.
- **The shared-`JsonObject` race is structurally eliminated** — each
  sink gets its own `DeepClone`; built-in and custom sinks may mutate
  freely.
- **`ParsedData`'s public shape and `CrawlStep` are unchanged.**
  Behaviourally-additive: `Data` now always carries `url` (byte-
  identical persisted output for the four sinks; a gain for Console).
  One narrow edge — constructing a `ParsedData` mutates the passed
  `JsonObject`.
- **The `IScraperSink` contract** documents the own-copy / mutate-freely
  guarantee; a custom sink need not merge the URL.
- **`CONTEXT.md`** — the **ParsedData** glossary entry gains the
  construction-time-merge sentence and the reserved-`"url"`-key note;
  one new "Flagged ambiguities" bullet records the decision and the
  rejected paths.
- **SemVer: minor.** No public shape change; folds into the 10.0.0 wave.

## Implementation

All changes landed in one commit on `adr-0031-parseddata-url-merge`:

1. ✅ **`WebReaper/Sinks/Models/ParsedData.cs`** — the `Data` property
   is re-declared with an initializer that folds `Url` in via a private
   static `MergeUrl`. The public positional shape `(string Url,
   JsonObject Data)` is unchanged. XML docs name the construction-time
   merge and the reserved `"url"` key.
2. ✅ **`WebReaper/Sinks/Concrete/BufferedFileSink.cs`,
   `WebReaper.Cosmos/CosmosSink.cs`, `WebReaper.Mongo/MongoDbSink.cs`,
   `WebReaper.Redis/RedisSink.cs`** — each drops its
   `entity.Data["url"] = entity.Url;` line. `CosmosSink` keeps its
   `["id"]` write (the `/id` partition key). `ConsoleSink` is unchanged
   in code and now prints the URL for free.
3. ✅ **`WebReaper/Core/ScraperEngine.cs`** — `ProcessTargetPage` clones
   `Data` per sink at the fan-out:
   `result with { Data = (JsonObject)result.Data.DeepClone() }`.
4. ✅ **`WebReaper/Sinks/Abstract/IScraperSink.cs`** — `EmitAsync`'s
   summary gains the own-copy / URL-already-present / mutate-freely
   sentence.
5. ✅ **Tests** — new `ParsedDataConstructionTests` (4 tests: the
   constructed `Data` carries `url`; the typed `Url` accessor is
   preserved; the merge is idempotent; a Schema field named `url` is
   overwritten); `Each_sink_receives_its_own_clone_of_the_parsed_data`
   in `ScraperEngineDriverTests` (two sinks receive distinct
   `ParsedData` with distinct `Data`, both carrying the merged `url`).
   No dedicated `ConsoleSink`-output test: *"Console gains the URL"* is
   a consequence of `Data` carrying `url` (pinned by
   `ParsedDataConstructionTests`) and `ConsoleSink` printing `Data`
   verbatim (unchanged code) — a `Console.Out`-redirection test would
   add global-state fragility to re-verify a non-bug.
6. ✅ **`CONTEXT.md`** — the **ParsedData** glossary entry updated; one
   new "Flagged ambiguities" bullet capturing the decision and the six
   rejected paths.

### Guardrails (run on the branch tip)

- `dotnet build WebReaper.sln` — **0 errors**. Inspection of the
  warning list confirms none references a file ADR-0031 touches;
  `WarningsAsErrors=CS1591` on core is green (the re-declared `Data`
  property carries its XML documentation).
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — **153/153 pass**
  (148 pre-0031 + 4 new `ParsedDataConstructionTests` + 1 new
  `ScraperEngineDriverTests` case); no previously-passing test
  regresses — the four sink unit tests still see `url` in the output
  because `ParsedData`'s construction now provides it.
- `dotnet test WebReaper.Tests/WebReaper.Sqlite.Tests` — **10/10 pass**
  (unaffected; ADR-0031 does not touch the scheduler path).
- The satellite test projects (Cosmos / Mongo / Redis / Puppeteer /
  AzureServiceBus) compile (full-solution build green) and the
  live-site `WebReaper.IntegrationTests` run on CI.
