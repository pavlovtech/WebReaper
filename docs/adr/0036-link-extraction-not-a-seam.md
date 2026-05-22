# Link extraction collapses to a concrete function; the `ILinkParser` one-adapter seam is removed

## Status

**Accepted — implementation complete** (2026-05-22; landed on branch
`adr-0036-link-extraction-not-a-seam` off `origin/master`). Eleventh ADR of
the `/improve-codebase-architecture` review wave (after ADR-0026 through
ADR-0035, PRs #82-92, all merged), and **candidate #5 — the last — of the
2026-05-22 review**. Breaking: `ILinkParser` (a Tier-1 public seam interface,
ADR-0023) is removed. Folds into the unreleased 10.0.0 wave the user is
batching.

## Context

**Link discovery** is one half of crawling a page: given a loaded document
and a selector, find the follow / paginate URLs it points at. (The other
half — turning a target page into data — is the Schema fold, ADR-0002.)
Today it is a seam:

```csharp
public interface ILinkParser
{
    Task<List<string>> GetLinksAsync(Uri baseUrl, string html, string selector);
}
```

with one implementation — `LinkParserByCssSelector`, an `internal` class of
~8 lines of AngleSharp LINQ.

Measured against
[LANGUAGE.md](../../.claude/skills/improve-codebase-architecture/LANGUAGE.md),
this is a **hypothetical seam**, not a real one:

- **One adapter.** `LinkParserByCssSelector` is the only `ILinkParser`. *One
  adapter is a hypothetical seam* — the same line ADR-0032 rejected an
  `IStopRule` on.
- **One caller.** `CrawlStep` is the only consumer — two call sites
  (`StepAsync`'s follow-link and pagination-link extraction).
- **No way to supply another.** `SpiderBuilder` wires its runtime
  collaborators — content parser, link tracker, cookie storage, retry
  policy, page loader, logger, proxy provider — each behind a `With*`
  registration method (ADR-0009). `ILinkParser` is the **one exception**: a
  hardcoded `private ILinkParser LinkParser { get; } = new
  LinkParserByCssSelector();`. There is no `WithLinkParser`; ADR-0009's
  registration seam never listed one. `ILinkParser` is a public Tier-1
  interface (ADR-0023) a consumer can *see* in the API and *implement* — but
  cannot *supply*.
- **No test binds it.** `CrawlStepTests` constructs the real
  `LinkParserByCssSelector`; nothing mocks the interface. The interface is
  not the test surface — the concrete is.

**The deletion test.** Delete `ILinkParser`: does complexity reappear across
N callers, or vanish? It vanishes. There is one caller; link extraction is a
pure, stateless function (HTML + selector in, URLs out —
`LinkParserByCssSelector` has no fields). The interface is pass-through
ceremony — an `interface` keyword and an `Abstract/` file standing between
`CrawlStep` and an 8-line function.

**ADR-0002 saw this and stopped one step short.** It already *named*
`ILinkParser` "a single-adapter seam … indirection without variation" — but
the question ADR-0002 was answering was *"should link discovery be expressed
through `ISchemaBackend`?"* (no — it has no tree to fold). It concluded "keep
it separate from the Schema fold", and left it an interface. *Whether it
should be an interface at all* was never asked. This ADR asks it.

**A latent crash rides along.** `LinkParserByCssSelector` does
`.Select(e => e.Attributes["href"]?.Value).Select(l => new Uri(baseUrl,
l).ToString())`. An `<a>` element matched by the selector but carrying no
`href` yields a `null`, and `new Uri(baseUrl, null)` throws
`ArgumentNullException` — mid-crawl, on any page whose link selector matches
a hrefless anchor. Not hypothetical: `<a name="…">` anchors and JS-driven
`<a>`-without-`href` are common.

## Decision

**Two moves.**

### 1. The seam collapses — link extraction is a concrete function

`ILinkParser` is removed. `LinkParserByCssSelector` becomes `LinkExtractor`
([WebReaper/Core/Parser/Concrete/LinkExtractor.cs](../../WebReaper/Core/Parser/Concrete/LinkExtractor.cs))
— an `internal static` class with one `static` method:

```csharp
internal static class LinkExtractor
{
    public static Task<List<string>> GetLinksAsync(Uri baseUrl, string html, string cssSelector);
}
```

`CrawlStep` calls `LinkExtractor.GetLinksAsync(...)` directly; its
constructor narrows from `(ILinkParser, IJsonContentParser)` to
`(IJsonContentParser)`. `SpiderBuilder`'s hardcoded `LinkParser` property is
deleted.

**Link discovery stays a concern of its own.** `LinkExtractor` is its own
file, distinct from the Schema fold — ADR-0002's separation is preserved.
What changes is only that the concern is a *function*, not a *seam*: there is
no `interface`, no `Abstract/` declaration, no builder knob. The
"ByCssSelector" qualifier is dropped from the name — it implied a "ByXPath" /
"ByJsonPath" sibling that never existed, and that false-variation
implication is exactly what this ADR removes.

`LinkExtractor` is **Tier-2 internal** (ADR-0023): reached only by
`CrawlStep`, named by nobody public.

### 2. The no-`href` crash is fixed

`GetLinksAsync` filters elements whose `href` is absent, empty, or
whitespace *before* the `new Uri` resolution — they are skipped, not crashed
on. The fix is part of the rewrite, not a separate patch: the collapse
touches the one function anyway, and a hrefless `<a>` is exactly the "no
usable link here" case the function should silently pass over.

### Bounded scope — what this does NOT change

- **The crawl behaviour.** `LinkExtractor` performs the same AngleSharp
  query and the same base-URL resolution as `LinkParserByCssSelector`; the
  only behaviour delta is hrefless anchors going from *crash* to *skipped*.
  `CrawlStepTests` / `SpiderTests` pass with their existing HTML fixtures.
- **Link discovery's separation from content parsing.** ADR-0002's "two
  halves of crawling" stands — `LinkExtractor` is a separate module from the
  Schema fold, not folded into it and not folded into `CrawlStep`.
- **`IJsonContentParser`.** The *content* seam genuinely varies — three
  backends (HTML/CSS, HTML/XPath, JSON) reached via `WithContentParser` /
  `WithJsonContentParser` / `WithXPathContentParser`. It is a real seam and
  is untouched. Collapsing the hypothetical seam while keeping the real one
  is the point.
- **`CrawlStep` / `ICrawlStep`.** `CrawlStep` keeps its `ICrawlStep` seam
  and its `IJsonContentParser` dependency; only the `ILinkParser` parameter
  is gone.

## Considered options

### (a) Complete the seam — add `WithLinkParser(ILinkParser)`

Make `ILinkParser` pluggable like the other `SpiderBuilder` collaborators:
keep the interface, add a `WithLinkParser` registration method.
**Rejected:** it does not make the seam *real* — it makes a hypothetical
seam *reachable*. With still one adapter, `WithLinkParser` is a permanent
public API surface (SemVer-committed forever) added for a consumer need no
consumer ever expressed — speculative generality, the anti-pattern
LANGUAGE.md and the ADR-0026 / ADR-0032 discipline reject. ADR-0009
enumerated the registration seam's methods and `WithLinkParser` was
deliberately not among them.

### (b) Fix only the crash, keep `ILinkParser`

Patch the `null`-`href` `new Uri` and leave the seam. **Rejected:** it punts
the architecture. The crash is a bug; the *shape* — a public interface a
consumer can see but not supply, one adapter, one caller — is the friction
candidate #5 names. Fixing the bug and stopping treats the symptom and
leaves the hypothetical seam for the next review to re-surface.

### (c) Inline link extraction into `CrawlStep`

Drop the interface *and* the separate class — make link extraction a
`private` method of `CrawlStep`. **Rejected:** it over-collapses. ADR-0002
keeps link discovery a separate *concern* from content parsing; a `private`
method buries that concern inside the crawl-step decision. `LinkExtractor`
as its own file keeps the concern visible and independently testable — the
collapse is of the *seam* (the interface), not of the *module* (the
function).

### (d) Keep `ILinkParser` for a future XPath / JSONPath link extractor

Crawling a JSON API cannot follow links today (link discovery is HTML/CSS
only — ADR-0007 parked a JSON variant as out of scope); keep the interface
as the seam that variant will plug into. **Rejected:** the anticipated
second adapter probably never arrives *in this shape*. `GetLinksAsync(Uri,
string html, string cssSelector)` is HTML-shaped — an HTML document string
and a CSS selector. JSON link-following in WebReaper naturally rides the
Schema fold: extract URL fields as *content* via `IJsonContentParser`, the
route ADR-0007 already implies. A JSONPath link extractor would not fit this
signature. And the principle: when a genuinely different link-discovery
shape *does* arrive, the interface is extracted *then* — from two real
implementations, generalised correctly — not guessed at now from one. A seam
waits for its second adapter.

## Consequences

- **Link discovery has one home and no indirection.** `CrawlStep` →
  `LinkExtractor.GetLinksAsync` is a direct static call; the `interface`,
  the `Abstract/` file, and the `SpiderBuilder` property are gone. The
  8-line function is no longer wrapped in a seam as complex as itself.
- **`SpiderBuilder` is uniform.** Every runtime collaborator it wires now
  either genuinely varies and has a `With*` method, or is the one fixed
  function — no hardcoded-`private`-property odd-one-out masquerading as a
  seam.
- **A mid-crawl crash is fixed.** A link selector matching a hrefless `<a>`
  no longer throws `ArgumentNullException`; the element is skipped. Pinned
  by a new `CrawlStepTests` case.
- **`ILinkParser` is a breaking change.** It is a Tier-1 public seam
  interface (ADR-0023). SemVer **major** — folds into the unreleased 10.0.0
  wave. Blast radius is minimal: a repo-wide sweep finds no satellite,
  Example, or `Misc` code names `ILinkParser` or `LinkParserByCssSelector`;
  only code that implemented `ILinkParser` (no in-repo code did) or named
  the internal adapter breaks. There was no `WithLinkParser`, so no
  fluent-path migration exists to break.
- **Relationship to ADR-0023.** ADR-0023's Tier-1 `*/Abstract` enumeration
  lists `ILinkParser` among the seam interfaces. That enumeration is a
  2026-05-19 snapshot; this ADR applies ADR-0023's *own* instrument — the
  deletion test — to that one entry and finds it is not contract. ADR-0023's
  decision (the public surface *is* the contract, drawn by the deletion
  test) is not reversed; it is honoured. `ILinkParser` simply fails the test
  ADR-0023 established.
- **Relationship to ADR-0002.** ADR-0002 kept link discovery separate from
  the Schema fold and named it a single-adapter seam; this ADR finishes the
  thought. Separate concern: yes, preserved. Interface: no — a single-adapter
  seam with no variation is the indirection ADR-0002's own words condemn.
- **CONTEXT.md** gains a "Flagged ambiguities" bullet recording the
  decision.
- **AOT is unaffected.** Removing an interface and making a class static
  adds no reflection, serialization, or dynamic codegen — strictly less
  surface to analyse. `WebReaper.AotSmokeTest` confirms it.

## Implementation

Landed on `adr-0036-link-extraction-not-a-seam`:

1. **`WebReaper/Core/Parser/Abstract/ILinkParser.cs`** — deleted.
2. **`WebReaper/Core/Parser/Concrete/LinkParserByCssSelector.cs`** —
   deleted; replaced by
   **`WebReaper/Core/Parser/Concrete/LinkExtractor.cs`** (new) — the
   `internal static` link-extraction function. Elements with an absent /
   empty / whitespace `href` are filtered before `new Uri` resolution (the
   crash fix).
3. **`WebReaper/Core/Crawling/Concrete/CrawlStep.cs`** — the `ILinkParser`
   field and constructor parameter removed; the two call sites call
   `LinkExtractor.GetLinksAsync` directly; the class doc updated.
4. **`WebReaper/Builders/SpiderBuilder.cs`** — the hardcoded `LinkParser`
   property removed; `Build()` constructs `new CrawlStep(ContentParser)`.
5. **`WebReaper/Core/Parser/Abstract/IJsonContentParser.cs`** — the
   `<see cref="ILinkParser"/>` doc reference (a dangling cref once the type
   is gone) rephrased to prose.
6. **Tests** — `CrawlStepTests` / `SpiderTests` drop the
   `LinkParserByCssSelector` constructor argument; a new
   `CrawlStepTests.Transit_page_skips_anchors_with_no_usable_href` pins the
   crash fix.
7. **`CONTEXT.md` / `CLAUDE.md` / `README.md`** — a new "Flagged
   ambiguities" bullet; the architecture note's "link extraction is
   `ILinkParser`" updated; the `ILinkParser` row removed from the README
   interfaces table.

### Guardrails (run on the branch tip)

- `dotnet build WebReaper.sln` — **0 errors**; 19 warnings, all
  pre-existing — unchanged in set and count from ADR-0035 (NU1510, CS8633,
  CS8618, CS8602, CS8604, CS1574, xUnit1031). None references a file this
  ADR touches; core `WarningsAsErrors=CS1591` stays green — `LinkExtractor`
  and `CrawlStep` are Tier-2 internal (CS1591 does not fire on them), and
  removing the documented `ILinkParser` leaves no undocumented gap.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — **163/163 pass** (162
  + 1: the new hrefless-anchor case). `CrawlStepTests` and `SpiderTests`
  pass with their existing fixtures — the collapse is behaviour-preserving
  for well-formed HTML.
- `WebReaper.AotSmokeTest` — `dotnet publish` (Native-AOT) emits no IL-trim
  warnings, and the published native binary prints **`AOT SMOKE: ALL PASS`**
  (9/9). Removing an interface is AOT-neutral.
- The network-backed satellite suites and the live-site
  `WebReaper.IntegrationTests` (the real crawl, exercising `LinkExtractor`)
  run on CI. No satellite source changed — the whole-solution build proves
  every satellite and Example still compiles.

## References

- ADR-0002 — the Schema fold and node-backend seam; it kept link discovery
  separate from the fold and named `ILinkParser` "a single-adapter seam …
  indirection without variation". This ADR finishes that observation.
- ADR-0023 — the Tier-1 / Tier-2 doc contract drawn by the deletion test;
  `ILinkParser` was Tier-1, and this ADR re-runs that test on it.
- ADR-0032 — rejected an `IStopRule` because "one adapter is a hypothetical
  seam (LANGUAGE.md)"; the identical reasoning, applied to link extraction.
- ADR-0009 — the registration seam: the `With*` methods over role
  interfaces. `WithLinkParser` was never among them — the absence this ADR
  makes structural.
- LANGUAGE.md — the deletion test; *one adapter = hypothetical seam, two
  adapters = real seam*; shallow module.
