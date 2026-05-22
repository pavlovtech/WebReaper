# `IJsonContentParser` becomes `IContentExtractor`; the three `*ContentParser` shells collapse onto `SchemaFold`

## Status

**Accepted — implementation complete** (2026-05-23; landed on branch
`adr-0039-content-extractor-seam` off `origin/master`). **Candidate #3 — the
last — of the 2026-05-22 final pass** (#1 → ADR-0037, #2 → ADR-0038, #3 →
this), the pass taken before the library's AI-native work. Breaking:
`IJsonContentParser`, `SchemaContentParser<TNode>` and `WithContentParser`
are Tier-1 public surface (ADR-0023). Folds into the unreleased 10.0.0 wave.

## Context

**Content extraction** is one half of crawling a page: given a loaded target
page and a `Schema`, produce the structured record the sinks emit. (The other
half — link discovery — collapsed to the concrete `LinkExtractor` function in
ADR-0036.) Today the seam is `IJsonContentParser`:

```csharp
public interface IJsonContentParser
{
    Task<JsonObject> ParseToJsonAsync(string content, Schema? schema);
}
```

Measured against
[LANGUAGE.md](../../.claude/skills/improve-codebase-architecture/LANGUAGE.md)
and [CONTEXT.md](../../CONTEXT.md), it has two naming faults and one
shape fault.

**The "Json" qualifier is a fossil.** ADR-0008 introduced
`IJsonContentParser` / `ParseToJsonAsync` / the `JsonObject` terminal — the
"Json" marked the new System.Text.Json output, distinguishing it from the
Newtonsoft `JObject`-returning `IContentParser` it replaced. `IContentParser`
was removed outright at the 6.0.0 major. The qualifier now disambiguates
against **nothing** — and it actively collides with the class
`JsonContentParser`, where "Json" is the *input* format. The interface's
"Json" is the output; the class's "Json" is the input; one word, two axes,
adjacent files. (ADR-0036 dropped the "ByCssSelector" qualifier from
`LinkParserByCssSelector` for implying a sibling that never existed; here the
sibling *did* exist, was removed, and the fossil stayed.)

**The three `*ContentParser` types are shallow shells.**
`AngleSharpContentParser`, `XPathContentParser` and `JsonContentParser` (all
`internal`) are each, in full: a field holding a `SchemaContentParser<TNode>`,
an `(ILogger)` constructor that `new`s it with one backend, and one method
that delegates. `SchemaContentParser<TNode>` *already implements*
`IJsonContentParser`. The **deletion test**: delete `XPathContentParser` — its
sole content, `new SchemaContentParser<IParentNode>(new
AngleSharpXPathSchemaBackend(), logger)`, moves to its sole caller,
`SpiderBuilder.WithXPathContentParser()`. Complexity does not reappear across
N callers; it relocates one line. Pass-through ceremony — a module whose
interface is as complex as its implementation.

ADR-0002 created the shells deliberately, and said so: it kept
`AngleSharpContentParser` / `JsonContentParser` "as thin shells" with "their
exact `(ILogger)` constructors" to stay **source-compatible** when the fold
was de-duplicated. That was a real concession then. ADR-0023 then made every
`*/Concrete` type `internal` — and the moment the shells went internal, the
source compatibility they preserved became unreachable from outside the
assembly. The concession has outlived the thing it conceded to.

**The shells disguise the seam's true shape.** They make three *backends*
look like three *adapters of `IJsonContentParser`*. They are not: there is
one fold (`SchemaContentParser<TNode>`) parameterised by three
`ISchemaBackend<TNode>` backends. `IJsonContentParser` has exactly **one**
adapter. The variation that appears to live at `IJsonContentParser` actually
lives one level down, at `ISchemaBackend`.

**A latent doc lie rides along.** `IJsonContentParser`'s XML doc states "A
`null` `schema` means no extraction (an empty object)." `SchemaContentParser
.ParseToJsonAsync` opens with `ArgumentNullException.ThrowIfNull(schema)`. The
documented contract and the code contradict each other — an extractor author
coding to the doc gets a surprise throw.

This is the final candidate of the final pass, and it is also the seam an
LLM-backed extractor will implement. Getting its name and shape honest
**now** — before the AI-native work begins — avoids renaming a just-shipped
public surface in a few weeks.

## Decision

Four moves; the seam stays a seam.

### 1. Rename the seam honestly

`IJsonContentParser` → `IContentExtractor`; `ParseToJsonAsync` →
`ExtractAsync`; the parameter `content` → `document`. `IContentExtractor`
pairs with `LinkExtractor` (ADR-0036) — ADR-0002's "two halves of crawling a
page" become nominally parallel, a real navigability gain for a reader (human
or AI) learning the codebase.

### 2. Rename the fold

`SchemaContentParser<TNode>` → `SchemaFold<TNode>`
([WebReaper/Core/Parser/Concrete/SchemaFold.cs](../../WebReaper/Core/Parser/Concrete/SchemaFold.cs)).
The class *is* the **Schema fold** — the CONTEXT.md term, ADR-0002's own
title — and "ContentParser" carried the same fossil verb the seam rename
removes. `SchemaFold<TNode>` stays **public**: it is the ADR-0002
custom-backend reuse vehicle.

### 3. Collapse the three shells

`AngleSharpContentParser` / `XPathContentParser` / `JsonContentParser` are
deleted. `SpiderBuilder` constructs the fold directly — e.g.
`WithJsonContentParser()` becomes `ContentExtractor = new
SchemaFold<JsonNode>(new JsonSchemaBackend(), Logger)`. This is exactly the
extension idiom ADR-0002 documents (`new SchemaFold<TNode>(backend, logger)`)
and that `SchemaFoldTests` already uses for its custom backend — the built-in
backends now use the same idiom the custom-backend test does. The registration
method `WithContentParser` → `WithContentExtractor`, to match the seam.

The no-argument `WithJsonContentParser()` / `WithXPathContentParser()` keep
their names — see Considered options (c).

### 4. Fix the doc lie

`IContentExtractor.ExtractAsync`'s doc states `schema` is required and
documents the `ArgumentNullException` the fold throws — the doc now matches
the code. The signature keeps `Schema?` (the nullability is vestigial, it
flows from `ScraperConfig.ParsingScheme`; tightening it to non-null `Schema`
is an ADR-0025-adjacent change, deliberately out of scope).

### The seam stays a seam — why this is not ADR-0036

After the collapse, `IContentExtractor` has one in-repo adapter
(`SchemaFold<TNode>`). ADR-0036 *removed* `ILinkParser` for being a
one-adapter seam. Three things make this seam genuinely different:

- **`WithContentExtractor` is an existing, public, used registration
  method.** `ILinkParser` had no `WithLinkParser` — ADR-0036 rejected
  *adding* one as speculative generality. Removing `IContentExtractor` would
  delete an extension point consumers already have.
- **The second adapter fits the signature and is imminent.** ADR-0036
  rejected keeping `ILinkParser` because the hypothetical JSONPath link
  extractor "would not fit the HTML/CSS-shaped `GetLinksAsync(Uri, string,
  string)`." `ExtractAsync(string document, Schema? schema) → JsonObject`
  fits an LLM-backed extractor *exactly* — page content plus a schema in,
  structured data out — and the AI-native work is the project that begins
  right after this pass. "A seam waits for its second adapter"; this one is
  at the door.
- **The honest axis of variation is *strategy*, not *backend*.** ADR-0036
  kept this seam with the reason "the content seam varies — three backends."
  That reason was imprecise: the three backends vary at `ISchemaBackend`, not
  at this seam. `IContentExtractor` varies by extraction *strategy* — the
  deterministic Schema fold versus an LLM extractor — one level up. Collapsing
  the shells makes the true count visible (one strategy adapter today), and
  this ADR keeps the seam for the correct, honest reason.

### Bounded scope — what this does NOT change

- **The fold behaviour, `ISchemaBackend`, the backends, the `Schema`
  grammar** — untouched. `SchemaFold<TNode>` is `SchemaContentParser<TNode>`
  renamed, the fold logic byte-identical; every parser test passes on its
  existing fixtures.
- **`WithJsonContentParser` / `WithXPathContentParser`** keep their names.
- **The `Core/Parser` folder and namespace** — unchanged.
- **`ExtractAsync`'s shape** — no `CancellationToken` is added; see
  Considered options (d).

## Considered options

### (a) Keep the shells — rejected

They are pass-through ceremony; the deletion test concentrates nothing.
ADR-0002's "thin shell" justification was source compatibility, and ADR-0023
made `*/Concrete` internal — removing the very surface that compatibility
served. Keeping the shells leaves the seam's adapter count *looking* like
four when it is one, which is precisely the shape fault this candidate names.

### (b) Collapse the seam too, `ILinkParser`-style — rejected

Symmetric with ADR-0036 — but the two cases differ exactly where it matters:
`WithContentExtractor` already exists and is used (no `WithLinkParser` ever
did), and the second adapter (an LLM extractor) fits the exact signature and
is imminent (the JSONPath link extractor never fit `GetLinksAsync`'s shape).
ADR-0036's own option (d) — "the interface is extracted when a real second
shape arrives, generalised from two implementations" — argues *for* keeping a
seam whose second shape is now arriving. Removing the seam here and re-adding
it in a few weeks for the AI work would be churn.

### (c) Rename `WithJsonContentParser` / `WithXPathContentParser` too — rejected

"ContentParser" appears in the names, but "parse responses as JSON / with
XPath" is *honest* — these methods select a document backend, which is what
they do. They are the most-used, most-documented entry points (issue #27,
discussion #17, every `Examples/` project). Renaming them is churn with no
honesty gain: the fossil was the *interface*'s "Json" (disambiguating
nothing), not these methods' "Json" (which correctly names the input). The
small residual mismatch — `WithContentExtractor` beside
`WithJsonContentParser` — is the lesser evil against a wide doc/Example
churn.

### (d) Add a `CancellationToken` to `ExtractAsync` — rejected (deferred)

An LLM-backed extractor makes a long network call and would genuinely want a
cancellation token, and ADR-0037 just made cancellation the crawl's
termination spine. But the token does not currently reach
`CrawlStep.StepAsync`; giving it to `ExtractAsync` means threading it through
`ICrawlStep` and `ISpider` — a cross-cutting change made for a caller that
does not yet exist. ADR-0036's discipline applies: shape an interface *from*
its second adapter, not *for* it. The AI-features work adds the token when the
LLM extractor is in hand to prove the requirement and its exact shape.

### (e) Rename the `Core/Parser` namespace to `Core/Extraction` — rejected

Internally consistent, but every file in the folder and every `using` across
the solution changes for a gain a consumer barely perceives — the seam *type*
name carries the honesty; the namespace is secondary. Blast radius out of
proportion to the value.

## Consequences

- **The seam's name states its job.** `IContentExtractor` and `LinkExtractor`
  are the two nominally-parallel halves of crawling a page (ADR-0002).
- **The seam's true shape is visible.** One strategy adapter today
  (`SchemaFold`); the document-shape variation sits one level down at
  `ISchemaBackend`. An LLM-backed extractor has an obvious, named home —
  implement `IContentExtractor`, register with `WithContentExtractor`.
- **`SpiderBuilder` is uniform.** It constructs the fold the same way a
  consumer's custom-backend code does (ADR-0002's documented idiom) — no
  internal shell standing between the builder and the public fold.
- **The contract is honest.** `ExtractAsync` documents the
  `ArgumentNullException` the fold actually throws; the "null ⇒ empty object"
  lie is gone.
- **Breaking change.** `IJsonContentParser`, `SchemaContentParser<TNode>` and
  `WithContentParser` are Tier-1 public surface (ADR-0023). SemVer **major** —
  folds into the unreleased 10.0.0 wave. Blast radius is small: a repo-wide
  sweep finds no satellite, `Examples/` or `Misc/` code naming any of them
  (the whole-solution build, which compiles all of those, is green), and the
  fluent path consumers *do* use — `WithJsonContentParser` /
  `WithXPathContentParser` — is untouched. Only code that implemented
  `IJsonContentParser`, named `SchemaContentParser<TNode>`, or called
  `WithContentParser` breaks, with a mechanical rename.
- **Relationship to ADR-0002.** ADR-0002 placed the fold in
  `SchemaContentParser<TNode>` and kept the three named shells as a stated
  source-compat concession. This ADR renames the fold to its CONTEXT.md name
  and collects the concession ADR-0023 made dead. ADR-0002's node-backend
  seam (`ISchemaBackend`) and the fold's logic are untouched.
- **Relationship to ADR-0036.** ADR-0036 collapsed link extraction's shell
  *and* — link extraction being genuinely one-adapter — its seam. This ADR
  applies the shell-and-name half to the content side, which ADR-0036
  explicitly left a seam; and it corrects ADR-0036's imprecise reason ("three
  backends") for why the content seam is real. Not a reversal of ADR-0036 —
  the same discipline, sharpened.
- **Relationship to ADR-0008.** ADR-0008 named `IJsonContentParser` /
  `ParseToJsonAsync` for the System.Text.Json terminal. The terminal
  (`JsonObject`) is unchanged; only the now-fossil "Json" qualifier on the
  *seam* is dropped. ADR-0008's decision is honoured, not reversed.
- **CONTEXT.md** gains a **Content extractor** term, a relationship line, and
  a Flagged-ambiguities bullet.
- **AOT is unaffected.** Renaming types and deleting three pass-through
  classes is strictly less surface to trim/analyse; `WebReaper.AotSmokeTest`
  confirms it.

## Implementation

Landed on `adr-0039-content-extractor-seam`:

1. **`IJsonContentParser.cs` → `IContentExtractor.cs`** — renamed (`git mv`);
   `Task<JsonObject> ExtractAsync(string document, Schema? schema)`; the doc
   rewritten to describe the seam's purpose, the strategy-vs-backend axis, and
   the `ArgumentNullException` contract.
2. **`SchemaContentParser.cs` → `SchemaFold.cs`** — `SchemaContentParser<TNode>`
   → `SchemaFold<TNode>`; the `content` parameter → `document`; the fold logic
   byte-identical.
3. **`AngleSharpContentParser.cs` / `XPathContentParser.cs` /
   `JsonContentParser.cs`** — deleted.
4. **`SpiderBuilder`** — `WithJsonContentParser()` / `WithXPathContentParser()`
   and the `Build()` default construct `new SchemaFold<TNode>(backend,
   logger)` directly; `WithContentParser` → `WithContentExtractor`.
5. **`ScraperEngineBuilder` / `DistributedSpiderBuilder`** — `WithContentParser`
   → `WithContentExtractor`; the registration-method docs updated.
6. **`CrawlStep`** — the `IContentExtractor` field and constructor parameter;
   the class doc updated ("content extraction is the injected
   `IContentExtractor` seam").
7. **`ISchemaBackend` / the backends / `LinkExtractor`** — `<see cref>` doc
   references track the renames.
8. **Tests** — the `*ContentParser`-constructing factory methods in nine
   unit-test files plus `WebReaper.AotSmokeTest` construct `new
   SchemaFold<TNode>(backend, logger)`; assertions unchanged — the shells
   were pass-through, so the collapse is behaviour-preserving.
9. **`CONTEXT.md` / `CLAUDE.md` / `README.md`** — the **Content extractor**
   term and relationship line, the architecture note, the README interfaces
   table.

### Guardrails (run on the branch tip)

- `dotnet build WebReaper.sln` — **0 errors**; 18 warnings, all pre-existing
  (CS1574, CS8618, CS8633, CS8602, CS8604, xUnit1031) — unchanged in set and
  count from ADR-0038's build; this ADR's renames add none. Core
  `WarningsAsErrors=CS1591` stays green — `SchemaFold` / `IContentExtractor`
  are documented Tier-1, the deleted shells were Tier-2 internal.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — **169/169 pass**. The
  `*ContentParser`-collapsing factory edits are behaviour-preserving for every
  parser test (CSS, XPath, JSON, the typed fold, coercion failure, the
  custom-backend test).
- `WebReaper.AotSmokeTest` — `dotnet publish` (Native-AOT) emits no IL-trim
  warnings; the published native binary prints **`AOT SMOKE: ALL PASS`**
  (9/9), driving `SchemaFold.ExtractAsync` over both a trivial backend and
  the JSON backend.
- The network-backed satellite suites and the live-site
  `WebReaper.IntegrationTests` run on CI. No satellite source changed — the
  whole-solution build proves every satellite and `Examples/` project still
  compiles.

## References

- ADR-0002 — the Schema fold and node-backend seam; placed the fold in
  `SchemaContentParser<TNode>` and kept the three `*ContentParser` shells as a
  stated source-compat concession.
- ADR-0008 — the System.Text.Json typed pipeline; named `IJsonContentParser`
  / `ParseToJsonAsync` for the `JsonObject` terminal, the "Json" qualifier
  this ADR retires.
- ADR-0023 — the Tier-1 / Tier-2 doc contract; made `*/Concrete` internal,
  removing the source compatibility the shells preserved.
- ADR-0036 — link extraction collapses to a concrete function; the sibling
  decision, whose shell-and-name discipline this ADR applies to the content
  side, and whose "the content seam is a real seam" exemption this ADR
  honours and sharpens.
- ADR-0025 — staged builder entry; a crawl always carries a `Schema`, which
  is why `ExtractAsync`'s `schema` is required.
- LANGUAGE.md — the deletion test; the shallow module; *one adapter = a
  hypothetical seam, two adapters = a real seam*.
