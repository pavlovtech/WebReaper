# Shared raw-extraction helper for the AngleSharp-DOM backends — the markup-family skeleton, not the seam

## Status

**Accepted — implementation complete** (2026-05-20; landed on branch
`adr-0027-extractraw-skeleton` off `origin/master` d565f79, awaiting
merge — second slice of the fresh `/improve-codebase-architecture`
review wave, after ADR-0026 / PR #82). Internal-only refactor; no
public-surface change; ADR-0002 / ADR-0007 stances unchanged. Folds
additively into whatever release the user batches next.

## Context

ADR-0002 fixed `SchemaContentParser<TNode>` as the single home of the
**Schema fold** and made each **Node backend** (`ISchemaBackend<TNode>`)
the sole place backend-local quirks live; ADR-0007 added a third backend
(XPath) by reusing the fold without re-deriving it. The seam itself is
deep — it earns its keep.

But within the AngleSharp-DOM family there are now *two* backends
([WebReaper/Core/Parser/Concrete/AngleSharpSchemaBackend.cs:33-55](../../WebReaper/Core/Parser/Concrete/AngleSharpSchemaBackend.cs#L33-L55)
and
[WebReaper/Core/Parser/Concrete/AngleSharpXPathSchemaBackend.cs:49-63](../../WebReaper/Core/Parser/Concrete/AngleSharpXPathSchemaBackend.cs#L49-L63))
whose `ExtractRaw` bodies are the same three-arm dispatch:

```csharp
// CSS backend, lines 33-55:                   // XPath backend, lines 49-63:
var el = (IElement)node;                       var el = (IElement)node;
string? content;                                string? content;
if (element.Attr is not null) {                if (element.Attr is not null)
    if (element.Attr == "src")                     content = el.GetAttribute(element.Attr);
        element.Attr = "title";                else if (element.GetHtml == false)
    content = el.GetAttribute(element.Attr);       content = el.Text();
}                                              else
else if (element.GetHtml == false)                 content = el.InnerHtml;
    content = el.Text();                       return content ?? string.Empty;
else
    content = el.InnerHtml;
return content ?? string.Empty;
```

The only intended difference is the CSS backend's `src`→`title` rewrite
on the attribute path — the ADR-0007 quarantined quirk. The rest is the
**Raw value** grammar driven by `SchemaElement.Attr` / `SchemaElement.GetHtml`
against an AngleSharp `IElement`, identical in both backends.

Measured against LANGUAGE.md:

- **Two adapters, one body.** The CSS and XPath backends share a 7-line
  skeleton plus one one-line quirk. By LANGUAGE.md's "one adapter means a
  hypothetical seam, two adapters means a real one," the skeleton itself
  is a *real* repeated pattern; the absence of a home for it is the
  friction.
- **Deletion test on the duplication.** If you delete one backend's
  dispatch and try to recover it from the other, you have to remember
  whether *this* one carries the `src`→`title` quirk. There's no
  structural distinction; only the test pinning at
  [WebReaper.Tests/WebReaper.UnitTests/SchemaFoldTests.cs:94](../../WebReaper.Tests/WebReaper.UnitTests/SchemaFoldTests.cs#L94)
  for the CSS quirk and "the only legitimate behavioural difference"
  prose in `CONTEXT.md` carries the distinction. A future contributor
  writing a third AngleSharp-DOM backend (a `Fizzler` selector,
  `Sizzle`, …) re-derives the skeleton from one of the existing two and
  drifts.
- **The seam contract is fine; the *family-local code reuse* is the gap.**
  This is not "deepen ISchemaBackend"; it is "give the AngleSharp family
  one home for its markup-leaf grammar."

ADR-0002 said quirks are backend-local. The dispatch is not a quirk — it
is the AngleSharp-DOM family's expression of the **Raw value** grammar.
The misclassification is what produced the duplication.

## Decision

Introduce one **internal static helper** that owns the AngleSharp-DOM
markup-leaf grammar; each AngleSharp backend's `ExtractRaw` shrinks to
"apply this backend's quirks, then delegate."

1. New `WebReaper/Core/Parser/Concrete/AngleSharpRawExtractor.cs`
   (internal static, Tier-2 by ADR-0023's deletion test — named by no
   consumer, reached only by the two backends that share its grammar):

   ```csharp
   internal static class AngleSharpRawExtractor
   {
       public static string ExtractRaw(IElement el, SchemaElement element)
       {
           string? content;
           if (element.Attr is not null)
               content = el.GetAttribute(element.Attr);
           else if (element.GetHtml == false)
               content = el.Text();
           else
               content = el.InnerHtml;
           return content ?? string.Empty;
       }
   }
   ```

2. `AngleSharpSchemaBackend.ExtractRaw` becomes:

   ```csharp
   public object? ExtractRaw(IParentNode node, SchemaElement element)
   {
       // ADR-0007: this backend's quarantined legacy quirk (the XPath
       // backend deliberately does not copy it). Quirk first, shared
       // markup grammar second.
       if (element.Attr == "src") element.Attr = "title";
       return AngleSharpRawExtractor.ExtractRaw((IElement)node, element);
   }
   ```

3. `AngleSharpXPathSchemaBackend.ExtractRaw` becomes:

   ```csharp
   public object? ExtractRaw(IParentNode node, SchemaElement element)
       => AngleSharpRawExtractor.ExtractRaw((IElement)node, element);
   ```

4. `JsonSchemaBackend` is **unchanged**. Its `ExtractRaw` is a single
   `node.DeepClone()` — a *different* grammar (structured-native, not
   markup-text-attribute). The deepening is scoped to the AngleSharp
   family, where the duplication actually exists.

5. `ISchemaBackend<TNode>` is **unchanged**. ADR-0002's seam contract
   (one method returning a `Raw value` per leaf, backend-quirks live
   here) is preserved exactly. The helper is a *family-internal*
   collaboration, not a new seam.

6. The CSS `src`→`title` rewrite still happens **before** delegation
   (and still mutates the `SchemaElement.Attr` in place — pinned by the
   existing CSS fold test). The XPath backend never sees the rewrite,
   the JSON backend never sees the rewrite — exactly today's pinned
   ADR-0007 behaviour.

The change is internal-only on the public surface; observable behaviour
is unchanged on every test, by construction.

## Considered options

### (a) Push the dispatch into the Schema fold

Move the `Attr` / `GetHtml` / `text` branches out of `ExtractRaw` and
into `SchemaContentParser`. The fold would call three different
backend methods (`ExtractAttribute` / `ExtractInnerHtml` / `ExtractText`)
based on the SchemaElement. Rejected:

- The JSON backend has no concept of attribute or inner-HTML — its
  `ExtractRaw` is `node.DeepClone()` regardless of `Attr` / `GetHtml`.
  Forcing it to implement three meaningless methods makes the seam
  leaky: the fold would have to know which backends ignore the dispatch
  and which don't, or document an awkward "all three call sites collapse
  to the same operation" contract.
- The CSS-backend `src`→`title` quirk would have no expression — there
  would be no place to mutate `element.Attr` before the dispatch
  branched into "extract attribute."
- Contradicts ADR-0002's "quirks are backend-local" stance directly.

### (b) Abstract base class the two AngleSharp backends extend

`abstract class AngleSharpSchemaBackendBase : ISchemaBackend<IParentNode>`
with the dispatch as a sealed method and `protected virtual` quirk
hooks. Rejected: pulls inheritance into a place that has worked fine
without it; both backends already differ in `SelectMany` / `SelectOne`
substantially (XPath needs `ContextElement` resolution off `IDocument`,
CSS calls `QuerySelectorAll` directly), so the base class would either
host nothing else or force common `SelectMany` shape they do not share.
The static helper is the smaller, narrower cut.

### (c) Keep two copies, document the duplication

Leave the bodies as-is and tighten the test pinning to keep them in
sync. Rejected: pinning by test catches the symptom (drift) after the
fact; a helper makes the drift unrepresentable. The helper is 14 lines
and one file — cost is smaller than the test-suite expansion option.

### (d) Generalise the helper to all backends (`SchemaBackendRawExtractor`)

Make the helper an `IRawExtractor` seam that all three backends route
through. Rejected as broad-seam-over-narrow-pattern (LANGUAGE.md's "two
adapters means a real seam"): two AngleSharp markup backends are a real
pattern; introducing a third method on the seam to please a one-line
JSON backend that wouldn't use it is widening for the sake of symmetry.

## Consequences

- **One home for the AngleSharp-DOM markup-leaf grammar.** A bug in the
  `Attr` / `GetHtml` / `text` dispatch is fixed once; a new AngleSharp
  backend (Fizzler, …) calls `AngleSharpRawExtractor.ExtractRaw` and
  inherits the grammar for free.
- **The seam is unchanged.** `ISchemaBackend<TNode>` keeps its four
  methods; ADR-0002's contract is exactly preserved; consumers / satellites
  implementing their own backend see no public-surface change.
- **The CSS-backend quirk stays quarantined.** ADR-0007's "the XPath
  backend returns the attribute asked for" pin remains structural — the
  CSS backend mutates `element.Attr` before delegation; the XPath backend
  delegates without touching it; the JSON backend never sees the call.
- **Test surface gains a unit-of-shared-grammar.** A new
  `AngleSharpRawExtractorTests.cs` pins the three-arm dispatch at the
  helper's interface (one home, three asserts, offline). Existing
  `SchemaFoldTests` continue to pin the *cross-backend* behaviour
  including the CSS quirk — unchanged.
- **No SemVer impact.** Internal-only refactor; the public seam, the
  fold, every fluent builder method, and every observable per-test
  outcome are identical. Lands additively alongside whatever release
  the user batches next.

## Implementation status

All five planned changes landed in one commit on
`adr-0027-extractraw-skeleton`:

1. ✅ `WebReaper/Core/Parser/Concrete/AngleSharpRawExtractor.cs` — new
   `internal static` helper, one method `ExtractRaw(IElement, SchemaElement)`
   returning `string` (never null — a missing attribute is
   `string.Empty`). Documented to ADR-0023's Tier-2 bar (the helper is
   internal, but the XML doc explains scope so a future contributor
   reading it understands why JSON does not call it).
2. ✅ `WebReaper/Core/Parser/Concrete/AngleSharpSchemaBackend.cs` —
   `ExtractRaw` now: one line of `src`→`title` quirk + one delegation
   call. The quirk comment names the test that pins the in-place
   mutation, so a future "tidy this up" doesn't lose the ADR-0007
   contract.
3. ✅ `WebReaper/Core/Parser/Concrete/AngleSharpXPathSchemaBackend.cs`
   — `ExtractRaw` is a single expression-bodied delegation
   (`=> AngleSharpRawExtractor.ExtractRaw(...)`); the docstring above
   names XPath's own pinning test so the no-rewrite property remains
   structurally visible.
4. ✅ `WebReaper.Tests/WebReaper.UnitTests/AngleSharpRawExtractorTests.cs`
   — six tests pinning the helper at its interface: attribute path
   returns the requested attribute; missing attribute is `string.Empty`
   (not null); no-Attr default returns text (`GetHtml` is `false` by
   default); `GetHtml=true` returns inner HTML; the helper itself does
   not apply the CSS `src`→`title` quirk; the helper does not mutate
   `SchemaElement.Attr`.
5. ✅ `CONTEXT.md` — one **Update:** appended to the ADR-0007 bullet
   pointing at this ADR; one new "Flagged ambiguities" bullet pinning
   the decision plus the four rejected paths (fold-dispatch / abstract
   base / two copies + tests / broad-seam-over-narrow-pattern).

### Guardrails (run on the branch tip)

- `dotnet build WebReaper.sln` — **0 errors, 18 warnings** (every
  warning pre-existing on `origin/master`; no new warning attributable
  to this ADR). `WarningsAsErrors=CS1591` on core therefore green.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests --no-build` —
  **100/100 pass**: 94 pre-0027 unit tests (including the ADR-0007 CSS
  src→title pin and the XPath no-rewrite pin, both of which exercise
  the new delegation path indirectly) + 6 new `AngleSharpRawExtractorTests`.
- The behavioural-cross-backend test `Src_to_title_rewrite_is_quarantined_in_the_html_backend`
  passes by construction (the CSS backend still mutates `SchemaElement.Attr`
  to `"title"` before delegating; the helper itself does not touch
  `Attr`, so the XPath backend's no-rewrite property is also structural,
  not test-pinned alone).
- Live-site `WebReaper.IntegrationTests` not run on the branch — they
  hit `alexpavlov.dev` with real Puppeteer/Chromium and `Task.Delay` up
  to 25 s, slow and environmentally flaky; the CI workflow runs them on
  the PR.
