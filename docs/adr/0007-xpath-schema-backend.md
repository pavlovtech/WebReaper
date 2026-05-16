# XPath selectors are a third Schema backend, not a new parser

Discussion #17 (a maintainer poll) asked whether there is demand for XPath /
RegEx selectors. ADR 0002 built the answer in advance: the `Schema` fold is
one home and a selector language is an `ISchemaBackend<TNode>`, "an
implementation of this seam, not a re-derivation of the walk." Adding XPath is
therefore a backend, not a parser.

`AngleSharpXPathSchemaBackend : ISchemaBackend<IParentNode>` reuses the exact
AngleSharp DOM the default CSS backend already parses; only the four seam
primitives differ — `SelectMany` / `SelectOne` evaluate an XPath 1.0
expression instead of a CSS selector. `XPathContentParser` is the thin shell
(`new SchemaContentParser<IParentNode>(new AngleSharpXPathSchemaBackend(),
logger)`), and `WithXPathContentParser()` mirrors `WithJsonContentParser()` on
both builders. The fold, the `IsList` object/scalar handling, type coercion,
the missing-node policy, and the swallow-and-log scope are untouched and
unduplicated — the deletion test ADR 0002 set up still holds.

`TNode` is `IParentNode`, identical to the CSS backend, on purpose: the root
is the `IDocument` (so the fold's `IDisposable` dispose still runs) and a
matched `IElement` is the scope when recursing into an object list, so a
relative XPath (`.//span`) resolves against each match.

## Deliberate divergence (not a regression — see CONTEXT.md)

The CSS backend rewrites a requested `src` attribute to `title`. That is a
quarantined legacy quirk of *that* backend; ADR 0002's rule is that quirks are
backend-local and a new backend is not obliged to copy another's. The XPath
backend returns the attribute asked for. This is the *only* behavioural
difference and it is the correct one; it is pinned by a test.

## Dependency

`AngleSharp.XPath` 2.0.6 — the official AngleSharp org, MIT, verified on NuGet
to target `net10.0` and depend on `AngleSharp` **1.4.0**, the exact version
WebReaper already references. No version conflict, no second DOM. Its API
(`IElement.SelectNodes` / `SelectSingleNode`) was read from the pinned source
commit before adoption, not assumed.

## Bounded scope

This adds an XPath **content/extraction** backend only. Link discovery still
uses `LinkParserByCssSelector` (CSS), exactly as the JSON backend (issue #27)
left it — out of scope here, a separate concern. RegEx "selectors" were also
polled in #17 and are deliberately **not** added: a regex over serialized
markup has no node scope, so it does not fit the `ISchemaBackend` contract
(`SelectMany`/`SelectOne` return nodes the fold recurses into); CSS and XPath
cover structured extraction. Recorded so #17 is not re-opened for RegEx
without this reasoning.

## SemVer

Purely additive (`AngleSharp.XPath` dependency, `XPathContentParser`, two
builder methods, one backend). Minor bump: 5.0.0 → 5.1.0.

## Considered options

- **XPath as an `ISchemaBackend` over AngleSharp.XPath (chosen).** Reuses the
  fold and the existing AngleSharp DOM/dependency; one new small package from
  the same org; the seam ADR 0002 created is used exactly as intended.
- **HtmlAgilityPack XPath backend (rejected).** A second HTML DOM and a new
  unrelated dependency for a capability the existing AngleSharp DOM can do via
  a same-org extension pinned to our AngleSharp version.
- **RegEx selector backend (rejected).** No node scope; does not satisfy the
  seam contract; an anti-pattern for structured extraction (see Bounded scope).
