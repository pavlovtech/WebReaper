# PageAction becomes a closed sum of typed arms; the page-action enum and its untyped `object[]` are removed

## Status

**Accepted — implementation complete** (2026-05-22; landed on branch
`adr-0035-pageaction-closed-sum` off `origin/master`). Tenth ADR of the
`/improve-codebase-architecture` review wave (after ADR-0026 through
ADR-0034, PRs #82-91, all merged), and **candidate #4 of the 2026-05-22
review**. Breaking: `PageAction` (a Tier-1 public `Domain` record)
becomes an abstract closed sum, and `PageActionType` (a Tier-1 public
enum) is removed. Folds into the unreleased 10.0.0 wave the user is
batching.

## Context

A **PageAction** is one browser interaction performed on a dynamic page
before scraping — click, wait, scroll, evaluate JS, wait-for-selector,
wait-for-network-idle. Today:

```csharp
public enum PageActionType { Click, Wait, ScrollToEnd, EvaluateExpression,
                             WaitForSelector, WaitForNetworkIdle }   // 6 arms
public record PageAction(PageActionType Type, params object[] Parameters);
```

The `Type` and the `Parameters` are **decoupled**, and the untyped
`object[]` carries no record of *what shape `Parameters` must have for a
given `Type`*. That knowledge lives, uncross-checked by the compiler, in
**three** places:

- **The builder** — `PageActionBuilder.Click` adds `[selector]`,
  `.WaitForSelector` adds `[selector, timeout]`, `.Wait` adds `[ms]`, …
- **The codec** — `PageActionCodec`
  ([PageActionJsonConverter.cs](../../WebReaper/Serialization/Converters/PageActionJsonConverter.cs))
  is ~75 lines of per-value **kind-tagging** (`{"k":"s","v":…}` —
  n/s/b/i/d). Its own doc states why it exists: *"`Parameters` is a
  genuinely-polymorphic `object[]`; STJ's default rematerialises a
  `JsonElement`, on which `Convert.ToInt32` throws."* The whole machine
  is `object[]` tax.
- **The transport** —
  [BrowserPageLoadTransport.cs](../../WebReaper.Puppeteer/BrowserPageLoadTransport.cs)
  interprets actions through a `Dictionary<PageActionType, Func<IPage,
  object[], Task>>` with `Convert.ToInt32(data.First())` /
  `(string)data.First()` runtime casts.

And the dictionary has **four** entries for the **six** enum arms.
`PageActions[pageAction.Type]` throws a bare `KeyNotFoundException`
mid-crawl for `EvaluateExpression` and `WaitForSelector` — and both are
reachable from the **public** `PageActionBuilder`
(`.EvaluateExpression(expr)`, `.WaitForSelector(sel, timeout)`), each
with XML docs advertising it as a supported feature.

**This was parked twice as "a missing feature"** — ADR-0004 ("out of
scope for this deepening"), the 2026-05-20 plan, the ADR-0030 review.
It is not a missing feature: the **shape** is the defect. Wiring two
dictionary entries leaves the `object[]`, the kind-tagging codec, the
runtime casts, and the enum/dictionary parallel-list structure all
intact — it treats the symptom.

**The deletion test.** Make `PageAction` a closed sum with one arm per
action, each carrying *typed* fields. The `PageActionCodec`'s
value-kind-tagging — n/s/b/i/d — **vanishes**: a `string`/`int` record
field serializes natively. The transport's `Convert.ToInt32` /
`(string)` casts **vanish**: the field is already typed. The
six-vs-four mismatch **cannot exist**: the arms *are* the types — there
is no separate enum and no separate dispatch table to fall out of sync.
Complexity does not relocate; it is gone.

This is a **shallow, type-unsafe shape**. The deepening is the closed
sum.

## Decision

Four moves.

### 1. `PageAction` becomes a closed sum

`PageAction` becomes an `abstract record` with a private constructor and
six nested `sealed record` arms, each carrying its own typed fields —
the ADR-0001 `CrawlOutcome` pattern (a closed hierarchy: construct only
via the arms, not extensible):

```csharp
public abstract record PageAction
{
    private PageAction() { }
    public sealed record Click(string Selector) : PageAction;
    public sealed record Wait(int Milliseconds) : PageAction;
    public sealed record ScrollToEnd : PageAction;
    public sealed record EvaluateExpression(string Expression) : PageAction;
    public sealed record WaitForSelector(string Selector, int TimeoutMs) : PageAction;
    public sealed record WaitForNetworkIdle : PageAction;
}
```

`PageActionType` is **removed** — the arm *is* the discriminant; a
parallel enum is pure redundancy.

### 2. `PageActionBuilder` keeps its public surface

`PageActionBuilder`'s methods — `Click(string)`, `WaitForSelector(string,
int)`, the `Repeat*` combinators, … — are **unchanged in signature**;
only their bodies change (`new PageAction.Click(selector)` instead of
`new PageAction(PageActionType.Click, selector)`). The builder is the
taught, recommended way to construct page actions, so the common path is
source-compatible. The break falls only on code that constructs
`new PageAction(PageActionType.X, …)` directly or switches on
`PageActionType`.

### 3. The codec is rewritten — and simplifies

`PageActionCodec` is rewritten to a `type` discriminator plus the arm's
**typed fields** (`{"type":"click","selector":"…"}`). The per-value
kind-tagging is deleted — there is no `object[]` left to tag. This keeps
ADR-0008's posture: the polymorphic crawl-state members (`Schema`,
`SchemaElement`, the selector chain) are hand-written converters,
deliberately *not* STJ polymorphism attributes; `PageAction` stays a
hand-written converter, now a *smaller* one. `WebReaperJsonContext` and
`WebReaperJson` drop their `PageActionType` registrations.

### 4. The transport dispatches with a switch; the two unwired actions are wired

`BrowserPageLoadTransport`'s `Dictionary<PageActionType, …>` becomes a
`switch` over the arm types. Because the arms *are* the cases,
`EvaluateExpression` and `WaitForSelector` are no longer omissible — the
switch handles all six (`page.EvaluateExpressionAsync`,
`page.WaitForSelectorAsync` are one-line PuppeteerSharp calls). The gap
closes as a **consequence of the structure**, not as a separate feature
bolted on. A future un-handled arm hits an actionable
`ArgumentOutOfRangeException` naming the offending arm — not a bare
`KeyNotFoundException`.

### Bounded scope — what this does NOT change

- **`PageActionBuilder`'s public API.** Method signatures and the
  `List<PageAction>` return are unchanged.
- **The serialization *posture*.** Still a hand-written converter
  (ADR-0008); the wire format changes (a clean break — `PageAction`
  rides only in `ScraperConfig` / `Job`, batched into 10.0.0), but the
  approach does not.
- **The AOT story.** The converter stays hand-written — no reflection,
  no `MakeGenericType`, no nested `JsonSerializer` call into the arms.
  Core stays AOT-clean; `WebReaper.AotSmokeTest` exercises the new path.
- **`Job`, `ScraperConfig`, `LinkPathSelector`, `PageRequest`,
  `ICrawlStep`.** All carry `PageAction` / `List<PageAction>` by the
  *base* type — unchanged; they never see an arm.
- **The dispatch stays in the satellite.** `PageAction` is core; *how to
  perform it on an `IPage`* is `WebReaper.Puppeteer` — core cannot see
  PuppeteerSharp (ADR-0009).

## Considered options

### (a) Wire the two missing dictionary entries, keep the enum + `object[]`

The "missing feature" reading. **Rejected:** it treats the symptom. The
`object[]`, the ~75-line kind-tagging codec, the transport's runtime
casts, and the enum-and-dictionary-must-agree shape all remain — the
next arm re-opens the same gap.

### (b) A `Match` method / visitor for hard compile-time exhaustiveness

`PageAction` exposes a `Match` (one delegate per arm); a new arm changes
its signature, so every call site — across the core/satellite boundary —
is a hard compile error. **Rejected:** the closed sum *already* removes
the candidate's defect — there is no longer a parallel enum and
dictionary to disagree, only one set of arm types and one switch. A
`Match` would only guard a hypothetical *future seventh* arm, and a
six-delegate `Match` is ceremony beyond what the codebase's existing
closed sum, `CrawlOutcome`, carries (`CrawlOutcome` is consumed by a
plain `switch` / `is`). A `switch` with an actionable default is the
`CrawlOutcome`-consistent choice; the residual risk — a future arm
un-handled — is a typed throw naming the arm, surfaced on first use, not
a silent `KeyNotFoundException`.

### (c) STJ native polymorphism (`[JsonPolymorphic]` / `[JsonDerivedType]`)

Let System.Text.Json own the discriminator. **Rejected:** it departs
from ADR-0008's established grammar — every polymorphic crawl-state
member (`Schema`, `SchemaElement`, the selector chain) is a hand-written
converter, and `WebReaperJsonContext` explicitly states the polymorphic
members are "owned by hand-written converters … and are intentionally
NOT listed here." Attribute-driven polymorphism is a new mechanism with
its own source-gen + AOT surface to validate — an unneeded risk when the
hand-written converter *shrinks* under the closed sum anyway.

### (d) Delete the two unwired arms (`EvaluateExpression`, `WaitForSelector`)

If they were "never wired," drop them. **Rejected:** both are public
`PageActionBuilder` methods with XML docs — advertised, supported
surface. Deleting public builder methods is a worse break than wiring
two one-line PuppeteerSharp calls, and removes real capability.

## Consequences

- **Each action's shape lives in one place — its arm.** `PageAction.WaitForSelector(string
  Selector, int TimeoutMs)` *is* the spec; the builder, the codec and
  the transport all derive from it instead of re-encoding a convention.
- **The codec simplified.** The per-value kind-tagging (n/s/b/i/d) is
  deleted; each arm reads/writes its typed fields directly. The
  `object[]`-polymorphism defect `PageActionCodec` was built to paper
  over no longer exists.
- **The six-vs-four mismatch is structurally impossible.** There is no
  enum and no dispatch dictionary — one set of arm types, one switch.
  `EvaluateExpression` and `WaitForSelector` are wired.
- **The runtime casts are gone.** `Convert.ToInt32(data.First())` /
  `(string)data.First()` become typed field reads.
- **`PageAction` / `PageActionType` are a breaking change.** `PageAction`
  is an abstract closed sum; `PageActionType` is removed. Tier-1 public
  (`Domain`) → SemVer **major** → folds into the unreleased 10.0.0 wave.
  `PageActionBuilder`'s public API is unchanged — the fluent path needs
  no migration; only direct `new PageAction(PageActionType.X, …)` /
  `PageActionType` switches migrate.
- **The wire format changes.** A persisted `ScraperConfig` / `Job` from
  before 10.0.0 will not deserialize — acceptable in a major wave, and
  `PageAction` only ever appears inside those two payloads.
- **AOT is unaffected.** The converter is hand-written and
  reflection-free; `WebReaper.AotSmokeTest` exercises the
  `ScraperConfig` / `Job` round-trip through the new closed sum.
- **CONTEXT.md** gains a "Flagged ambiguities" bullet recording the
  decision.

## Implementation

Landed on `adr-0035-pageaction-closed-sum`:

1. **`WebReaper/Domain/PageActions/PageAction.cs`** — rewritten: an
   `abstract record` (private ctor) + six nested sealed-record arms with
   typed fields.
2. **`WebReaper/Domain/PageActions/PageActionType.cs`** — deleted.
3. **`WebReaper/Builders/PageActionBuilder.cs`** — method bodies
   construct the arm records; public signatures unchanged.
4. **`WebReaper/Serialization/Converters/PageActionJsonConverter.cs`** —
   `PageActionCodec` rewritten: a `type` discriminator + each arm's typed
   fields; the per-value kind-tagging removed.
5. **`WebReaper/Serialization/WebReaperJsonContext.cs` /
   `WebReaperJson.cs`** — the `PageActionType` `[JsonSerializable]` entry
   and the `JsonStringEnumConverter<PageActionType>` removed.
6. **`WebReaper.Puppeteer/BrowserPageLoadTransport.cs`** — the
   `PageActionType`-keyed dictionary becomes a `switch` over the arms
   with an actionable default; `EvaluateExpression` and `WaitForSelector`
   wired.
7. **Tests** — `PageActionBuilderTests`, `StjSerializationTests`,
   `PayloadShellTests`, `LinkPathSelectorConstructionTests`,
   `WebReaper.AotSmokeTest`, `SqliteSchedulerTests` migrated to the arm
   records and arm-type assertions.
8. **`CONTEXT.md`** — one new "Flagged ambiguities" bullet.

### Guardrails (run on the branch tip)

- `dotnet build WebReaper.sln` — **0 errors**; 19 warnings, all
  pre-existing — unchanged in set and count from ADR-0034. The closed
  sum and the rewritten codec add none: the new public `PageAction` and
  its six arms carry XML docs (core `WarningsAsErrors=CS1591` stays
  green), and the transport's `switch` *statement* emits no CS8509.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — **162/162 pass**
  (163 − 1: `PageActionBuilderTests` consolidates from five tests to
  four — the closed sum makes the #61 enum-tag-typo bug class
  structurally impossible, so the separate `WaitForNetworkIdle` guard is
  folded into the one comprehensive arm test).
- `dotnet test WebReaper.Tests/WebReaper.Sqlite.Tests` — **10/10 pass**:
  the offline durable-adapter satellite round-trips a `Job` carrying a
  `PageAction` arm through the SQLite scheduler and the new codec.
- `WebReaper.AotSmokeTest` — `dotnet publish` (Native-AOT, the
  IL2026/IL3050-family `WarningsAsErrors`) is **zero-warning**, and the
  published binary prints **`AOT SMOKE: ALL PASS`** (9/9, including the
  new "PageAction closed-sum arm round-trips" check). The rewritten
  converter is hand-written and reflection-free — AOT is unaffected.
- The network-backed satellite suites (`Redis` / `Mongo` / `Cosmos` /
  `AzureServiceBus`) and the live-site `WebReaper.IntegrationTests` —
  which exercises the real Puppeteer dispatch, now a `switch` — run on
  CI.

## References

- ADR-0001 — the closed `CrawlOutcome` sum; `PageAction` applies the
  same pattern (abstract record, private ctor, nested sealed arms).
- ADR-0004 — the one page-loader / transport seam; it parked the
  four-of-six page-action gap as out of scope. This ADR closes it
  structurally.
- ADR-0008 — the System.Text.Json crawl-state grammar; why the codec
  stays a hand-written converter (and simplifies) rather than adopting
  STJ polymorphism attributes.
- ADR-0009 — core / satellite split: `PageAction` is core, the dispatch
  is the `WebReaper.Puppeteer` satellite's; core cannot see `IPage`.
