# LinkPathSelector enforces its grammar at the construction site; the selector-chain DSL gets two named factories

## Status

**Accepted — implementation complete** (2026-05-21; landed on branch
`adr-0030-link-path-selector-construction-guards` off `origin/master`
c21c827 — fifth ADR of the fresh `/improve-codebase-architecture`
review wave, after ADR-0026 / PR #82, ADR-0027 / PR #83,
ADR-0028 / PR #84, and ADR-0029 / PR #85, all merged).
Surface-additive (two new static factories `LinkPathSelector.Follow`
and `LinkPathSelector.Paginate`) + one narrowly-breaking behaviour
change (a `LinkPathSelector` constructed with an empty selector, an
empty-but-non-null pagination selector, or non-empty `PageActions`
paired with `PageType.Static` now throws at the primary-constructor
call instead of failing late at the **Crawl step** or silently
dropping the actions). Folds into the 10.0.0 wave the user is
batching (already breaking via ADR-0025).

## Context

The **Selector chain** is WebReaper's *other* declarative DSL — the
sibling to **Schema** in the user-facing construction surface, and
the one CONTEXT.md calls equally load-bearing: *"its length and its
head's pagination flag* **are** *the crawl state."* Its element type
is the record

```csharp
public record LinkPathSelector(
    string Selector,
    string? PaginationSelector = null,
    PageType PageType = PageType.Static,
    List<PageAction>? PageActions = null);
```

— [LinkPathSelector.cs](../../WebReaper/Domain/Selectors/LinkPathSelector.cs). Its
grammar rules live nowhere on the construction surface. The fluent
**ConfigBuilder** does its own `ArgumentException.ThrowIfNullOrWhiteSpace`
guards in each of its four selector-chain entry points
([ConfigBuilder.cs](../../WebReaper/Builders/ConfigBuilder.cs)), but
only for its own arguments — not for the record itself. Three
pathological shapes a user can write today, all of which compile
cleanly:

```csharp
// (1) Empty selector — fails late at CrawlStep when the parser
// tries to apply it; the error site is in the page-parser, not at
// the line the user wrote the bad selector.
new LinkPathSelector("")

// (2) Pagination selector present but empty/whitespace — same late
// failure, also no signal that "you told me to paginate but didn't
// say how."
new LinkPathSelector("a.item", "")

// (3) PageActions specified with PageType.Static — silently dropped.
// The HttpPageLoadTransport ignores PageActions; the click never
// fires; the crawl returns wrong data with no error anywhere.
new LinkPathSelector("a.item", null, PageType.Static,
    new() { PageAction.Click(".accept") })
```

The fluent **ConfigBuilder** catches *some* of (1) and (2) — its
per-method `ThrowIfNullOrWhiteSpace` calls reject empty strings for
the two selector parameters. It does **not** catch (3) at all, and it
does not protect the two other construction paths:

- **The JSON codec.** [`SelectorChainJsonConverter.ReadSelector`](../../WebReaper/Serialization/Converters/ImmutableQueueJsonConverters.cs)
  calls the primary constructor directly with values pulled from the
  wire — a tampered or corrupt persisted Job rematerialises into a
  malformed `LinkPathSelector` that then fails far from the read.
- **Direct construction by consumer code.** A DIY-distributed worker
  that rematerialises Jobs from persisted state, or an Examples-style
  consumer building a `ScraperConfig` procedurally, bypasses the
  fluent builder entirely.

Measured against [LANGUAGE.md](../../.claude/skills/improve-codebase-architecture/LANGUAGE.md):

- **Locality is missing.** The construction interface (the record's
  primary constructor — the one home every path goes through) does
  not name the rules; the fluent builder does, partially, for its
  own surface only. The bug class — malformed selectors — has no
  test home at the construction interface.
- **The interface is not the test surface.** There is no test that
  exercises `LinkPathSelector`'s grammar at its construction
  interface; the empty-selector rejection is tested indirectly
  through `ConfigBuilder.Follow("")` and not at all for the
  pagination-empty or PageActions-with-Static rules.
- **The natural shape doesn't carry intent.** `new LinkPathSelector("a", "b")`
  reads as "two selectors" — nothing in the type signature says
  whether that's *follow then paginate* or *paginate with these two
  selectors*. The two semantically-distinct shapes have the same
  syntactic form.

**ADR-0028 just did the exact-same shape for `Schema`.** Selector
non-empty, leaf needs Selector, list container needs Selector —
enforced at `Schema.Add`, the one construction site for Schema; a
`Schema.ListOf` factory bundled the list-of-objects triple under one
name. The Selector chain is the other declarative DSL in the same
codebase. The pattern is hot.

**Deletion test on the four ConfigBuilder `ThrowIfNullOrWhiteSpace`
calls.** Today they are the only enforcement, *and only for the fluent
path*. Delete them: empty selectors flow through to the parser and
crash late; the JSON-codec and direct-construction paths were already
unguarded, so nothing changes for them; observable behaviour
regresses only for the fluent-path user. Move the guards to the
constructor instead: the eight calls (Follow×1, FollowWithBrowser×1,
Paginate×2, PaginateWithBrowser×2) collapse to zero, *every*
construction path inherits the safety, and the throw moves to the
line the user wrote the bad selector. The deletion concentrates
complexity at one site and removes it everywhere else — passes the
test.

## Decision

Move the selector-chain grammar's enforcement from the fluent builder
to the `LinkPathSelector` construction site, and give the two
intent-shapes names. Three additive changes plus one supporting
codec tightening:

1. **The `LinkPathSelector` primary constructor validates at the
   construction site.** A property initializer over a primary-ctor
   parameter is the idiomatic positional-record validation point — it
   runs on every construction path (direct `new`, the fluent
   `ConfigBuilder` methods, the JSON codec's `ReadSelector`). Three
   rules, each throwing `ArgumentException` (or its `ArgumentNullException`
   subtype) with a specific, actionable message:

   - **Selector non-empty.** `null`, empty string, or whitespace-only
     is rejected. The selector locates links; an absent value means
     "you told me to traverse but didn't say what."
   - **PaginationSelector non-empty when present.** A `null`
     `PaginationSelector` is *valid* — it is the plain-follow shape,
     and the JSON codec round-trips follow steps constantly, so the
     constructor must accept it. A non-`null` `PaginationSelector`
     that is empty or whitespace-only is rejected.
   - **PageActions has content iff PageType is Dynamic.** A non-empty
     `PageActions` list paired with `PageType.Static` is rejected.
     Empty (`null` or empty list) is allowed with any `PageType` —
     same posture as ADR-0028's *empty equals absent* rule for
     `Selector`. The rejection catches the silent feature-drop bug:
     `PageActions` is consulted only by `BrowserPageLoadTransport`; a
     consumer who specifies actions and forgets `PageType.Dynamic`
     gets a crawl that loads pages without ever running the actions,
     with no error anywhere. The fluent builder's
     `FollowWithBrowser`/`PaginateWithBrowser` set `PageType.Dynamic`
     automatically, so the rule only fires on direct construction or
     JSON rematerialise — exactly the two paths the fluent builder
     cannot protect.

2. **Two named static factories give the intent-shapes names.**
   Sibling pair to ADR-0028's `Schema.ListOf`:

   ```csharp
   public static LinkPathSelector Follow(string selector,
       PageType pageType = PageType.Static,
       List<PageAction>? actions = null);

   public static LinkPathSelector Paginate(string itemSelector,
       string paginationSelector,
       PageType pageType = PageType.Static,
       List<PageAction>? actions = null);
   ```

   `new LinkPathSelector("a.item", "a.next")` reads as "two
   selectors"; `LinkPathSelector.Paginate("a.item", "a.next")` reads
   as "paginate." `Follow` is the no-pagination shape and reads the
   same way. The primary constructor remains the round-trip target —
   the JSON codec and any procedural-construction consumer keep
   working unchanged.

   **`Paginate` carries the paginate intent and is stricter than the
   constructor.** Calling `Paginate` *declares* a pagination step, so
   a `null` `paginationSelector` is malformed there — it would
   silently degrade to a plain `Follow`. The primary constructor
   *cannot* reject a `null` `PaginationSelector` (that is the
   legitimate follow shape it must round-trip — rule 1), so the
   pagination selector's required-ness is enforced in the `Paginate`
   factory, where the intent lives. This split — *constructor is
   shape-agnostic, the intent-carrying factory is stricter* — is
   deliberate and is the one nuance the design pass missed; the
   existing `BuilderArgumentValidationTests` (which pins
   `Paginate(x, null)` ⇒ throw) caught it.

3. **`ConfigBuilder` selector-chain methods collapse to one-line
   factory delegations.** The four
   `Follow` / `FollowWithBrowser` / `Paginate` / `PaginateWithBrowser`
   methods each did their own `ThrowIfNullOrWhiteSpace` then
   `new LinkPathSelector(…)`. The eight `ThrowIfNullOrWhiteSpace`
   calls go to zero in this file; each body becomes one line through
   the appropriate factory. The XML `<exception>` tags on these
   methods stay — the observable exception type (`ArgumentException`)
   and trigger condition are unchanged; only the throw site moves.
   The "fail-fast, 8.0.0" docstring annotation is updated to
   "enforced at `LinkPathSelector` construction (ADR-0030)."

   The four `ConfigBuilder` methods themselves stay — they are the
   canonical fluent surface, and `*WithBrowser` documents the
   satellite dependency in the signature itself (ADR-0009's
   load-bearing discoverability — see considered option (d)).

4. **The JSON codec gains one `JsonException` site.** `ReadSelector`
   initialises `string sel = ""` as a deserialise default; under the
   new grammar that empty string would trigger the constructor's
   selector-non-empty guard, which is correct but the exception loses
   the JSON property name. A `JsonException("missing or empty
   'selector' on a LinkPathSelector entry")` check before
   construction (same posture as the existing
   `throw new JsonException("expected array")` lines) gives the
   operator triaging a malformed persisted Job the JSON property
   name. The constructor guards remain the last-line defence for the
   other two rules.

This is internal-only on the *structure* of the public surface (no
new types beyond the two factories; no public-method removal);
behaviour on a malformed `LinkPathSelector` changes from *"late
throw at CrawlStep / silent feature-drop at the transport"* to
*"specific throw at the construction site."* That is the breaking
edge.

## Considered options

### (a) Factory-only guards, primary constructor stays open

Put the three rules inside `LinkPathSelector.Follow` / `.Paginate`
only; leave the primary constructor unvalidated. **Rejected:**
silently re-introduces the two paths the deepening is trying to
close — the JSON codec (`ReadSelector` constructs via `new`, not a
factory) and any direct-`new` consumer (a DIY-distributed worker
rematerialising Jobs, or a procedural `ScraperConfig` author).
ADR-0028's posture worked because `Schema.Add` is the **one**
construction site for Schema. `LinkPathSelector` has **multiple**
(record positional ctor + factory methods + JSON codec round-trip).
The primary-constructor guard is the only place all paths share.
Factory-only is a hypothetical seam (LANGUAGE.md: *"one adapter =
hypothetical, two = real"*); the constructor is the real one. (Note:
the `Paginate` factory's *additional* pagination-required guard —
decision 2 — is not "factory-only guards"; it is an intent-level
guard layered on top of the shared constructor guards.)

### (b) Reject any non-null PageActions regardless of content

Strictest version of rule 3: `PageActions != null` paired with
`PageType.Static` throws, even when the list is empty. **Rejected:**
breaks the "I cleared the actions but didn't null the list"
refactor — a consumer migrating Dynamic→Static by `.Clear()`-ing the
list (instead of reassigning `null`) would get a new throw with no
observable behaviour change. ADR-0028's *empty equals absent*
precedent argues against — `Schema.Add` treats an empty `Selector`
as missing, not as a strictly-different-from-null value. The grammar
should treat empty and absent identically; the silent-drop bug is
only present when actions actually exist to be dropped.

### (c) Allow PageType.Static with non-empty PageActions

Accept the status quo: `PageActions` is "intent metadata," the
transport decides relevance, and a Static transport silently
ignoring actions is the documented contract. **Rejected:**
re-admits the silent feature-drop bug — a consumer who specifies
actions wanted them to *fire*, not be ignored. The bug class is
exactly what ADR-0028 was designed to eliminate (*silent feature-
drop because the construction site didn't fail-fast*); allowing it
here loses the parallel entirely. The "intent metadata" framing also
fails LANGUAGE.md's *"the interface is what a caller must know"*
test — a caller cannot know that their `Click(".accept")` won't fire
without reading the transport's source.

### (d) Merge Follow / FollowWithBrowser (and Paginate / PaginateWithBrowser) into single overloaded ConfigBuilder methods

A single `Follow(string linkSelector, PageType pageType = PageType.Static, List<PageAction>? pageActions = null)`
covers both today's `Follow` and `FollowWithBrowser`. **Rejected:**
loses the *satellite-dependency-in-the-method-name* discoverability
that ADR-0009 made load-bearing. `FollowWithBrowser` reads as *"this
needs `WebReaper.Puppeteer` at runtime"* in the method name itself;
a parameter `PageType.Dynamic` does not. Same reasoning ADR-0028
used to keep `Schema.Add` and `Schema.ListOf` as separate names
rather than overload one.

### (e) Internalise `LinkPathSelector`'s primary constructor

Make the record's primary constructor `internal`, so external
construction must go through `.Follow(…)` / `.Paginate(…)`.
**Rejected:** consumer-supplied custom distributed patterns may
rematerialise Jobs from persisted state by calling the primary
constructor; forcing them through factory methods adds a
serialisation seam that wasn't there before. The primary-constructor
guards (rule 1) + the two factories (rule 2) together close the
friction *without* removing the construction surface. Follow-up
candidate if a deeper cut is later wanted, same posture as ADR-0028's
note on the `SchemaElement` parameterless ctor.

### (f) Keep ConfigBuilder's per-method `ThrowIfNullOrWhiteSpace` calls, add LinkPathSelector guards too

Defensive duplication — guards at both layers. **Rejected:** under
rule 1 the constructor throws with the same `ArgumentException`
type, the same trigger condition, and a message that names the
record property. The ConfigBuilder calls would be dead code — they
fire on the same inputs the constructor would catch a moment later.
Two-layer enforcement adds maintenance cost (eight call sites to
keep in sync with the record's rules) without observable benefit.
Collapse them; let the constructor speak.

### (g) Document the rules in CLAUDE.md and the record's XML summary; trust users to read the docs

The lowest-effort "fix." **Rejected** with the same reason ADR-0028
rejected its option (e): documentation can't fail-fast at the
keystroke. LANGUAGE.md is explicit — *"the interface is the test
surface"*; an invariant a user can violate without their code
failing locally is not an interface.

## Consequences

- **Malformed `LinkPathSelector`s fail fast at construction**, at
  the exact line the user (or codec, or distributed worker) wrote
  them. Three pathological shapes (empty `Selector` / non-null but
  empty `PaginationSelector` / non-empty `PageActions` with
  `PageType.Static`) all throw at construction.
- **Behaviour parity with Schema construction (ADR-0028)** — both
  declarative DSLs now have their grammar enforced at the
  construction site; both apply the same *empty equals absent*
  posture; both expose named factories for the load-bearing
  intent-shapes.
- **The Selector-chain DSL gains two names** — `LinkPathSelector.Follow`
  and `LinkPathSelector.Paginate` — for the two intent-shapes; the
  `Paginate` factory additionally requires the pagination selector
  (the constructor cannot, since `null` pagination is the valid
  follow shape it must round-trip).
- **ConfigBuilder reads cleaner.** Four methods collapse from
  "guard + construct" to "delegate to factory"; eight
  `ThrowIfNullOrWhiteSpace` calls go to zero in this file. The
  observable exception type, trigger, and SemVer surface are
  unchanged.
- **Tests gain a construction-side surface.** A new
  `LinkPathSelectorConstructionTests` pins every rule and both
  factories at the construction interface — offline, no parser
  instantiated, no document loaded.
- **The JSON codec gains one `JsonException` site** for a missing
  `selector`; a corrupt persisted Job fails at queue-read with the
  property name, not late at the crawl.
- **`CONTEXT.md` grows by one "Flagged ambiguities" bullet** and the
  **Selector chain** glossary entry gains a construction-site-
  invariants sentence, sibling to the **Schema** entry's ADR-0028
  reference.
- **SemVer: a narrowly-breaking change.** External code constructing
  a pathological `LinkPathSelector` today gets a new throw; the
  factories and the codec tightening are purely additive. Working
  code unchanged. Folds into the 10.0.0 wave; no separate release.

## Implementation

All changes landed in one commit on
`adr-0030-link-path-selector-construction-guards`:

1. ✅ `WebReaper/Domain/Selectors/LinkPathSelector.cs` — the record
   validates its primary-constructor parameters via property
   initializers (`Selector` and `PaginationSelector` through private
   `ArgumentException.ThrowIfNullOrWhiteSpace` helpers, `PageActions`
   through a cross-field check against `PageType`). Two new static
   factories `Follow` and `Paginate`; `Paginate` additionally guards
   `paginationSelector` non-null/non-empty (the intent-level rule the
   shape-agnostic constructor cannot carry). The record's XML summary
   names the construction-time invariants.
2. ✅ `WebReaper/Builders/ConfigBuilder.cs` — `Follow` /
   `FollowWithBrowser` / `Paginate` / `PaginateWithBrowser` drop their
   eight `ThrowIfNullOrWhiteSpace` calls; each body is one line
   through the appropriate factory. The XML `<exception>` tags point
   at `LinkPathSelector` construction (ADR-0030) as the throw site.
3. ✅ `WebReaper/Serialization/Converters/ImmutableQueueJsonConverters.cs`
   — `ReadSelector` throws `JsonException` on a missing/blank
   `selector`, before construction.
4. ✅ `WebReaper.Tests/WebReaper.UnitTests/LinkPathSelectorConstructionTests.cs`
   (new) — 15 test methods (17 cases counting `[Theory]` data)
   pinning every primary-constructor rule and both factories at the
   construction interface: empty / whitespace / null `Selector`;
   empty / whitespace / null `PaginationSelector` (and that the
   *constructor* accepts a `null` one while the *`Paginate` factory*
   rejects it); `PageActions` rejected with `Static`, accepted empty
   with `Static`, accepted non-empty with `Dynamic`; `Follow` /
   `Paginate` happy paths and their reject cases.
5. ✅ `WebReaper.Tests/WebReaper.UnitTests/StjSerializationTests.cs`
   — one sibling test: a tampered persisted Job (a chain entry with a
   blanked `selector`) throws `JsonException` at `DeserializeJob`.
6. ✅ `CONTEXT.md` — the **Selector chain** glossary entry gains a
   construction-site-invariants sentence naming
   `LinkPathSelector.Follow` / `.Paginate`; one new "Flagged
   ambiguities" bullet captures the decision and the seven rejected
   paths.

### Guardrails (run on the branch tip)

- `dotnet build WebReaper.sln` — **0 errors**. The warnings present
  are all pre-existing on `origin/master`; inspection of the warning
  list confirms none references a file ADR-0030 touches
  (`LinkPathSelector.cs`, the four `ConfigBuilder` methods,
  `ImmutableQueueJsonConverters.cs`, the two test files).
  `WarningsAsErrors=CS1591` on core therefore green: the two new
  factories and the three re-declared (validated) properties carry
  their XML documentation.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — **148/148
  pass** (130 pre-0030 + 17 new construction-test cases + 1 new
  `StjSerializationTests` case). The previously-failing
  `BuilderArgumentValidationTests.Follow_and_Paginate_reject_a_blank_selector(null)`
  — which pins `Paginate(x, null)` ⇒ throw — passes once the
  `Paginate` factory's pagination-required guard is in place; no
  other test regresses.
- `dotnet test WebReaper.Tests/WebReaper.Sqlite.Tests` — **10/10
  pass**; the Sqlite scheduler round-trips Jobs (carrying selector
  chains) and is unaffected.
- The satellite test projects (Puppeteer / Redis / Mongo / Cosmos /
  AzureServiceBus) compile (full-solution build green) and the
  live-site `WebReaper.IntegrationTests` run on CI — they construct
  selectors through the fluent builder, which this ADR does not
  break.
