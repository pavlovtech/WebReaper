# The Schema fold has one home; backends supply only document primitives

The recursive walk over a `Schema` tree — container vs object-list vs leaf vs
value-list, type coercion, the missing-node policy, the swallow-and-log scope —
lived **twice**: once in `AngleSharpContentParser` (HTML/CSS) and once in
`JsonContentParser` (JSON/JSONPath, shipped a week earlier for issue #27 by
re-deriving the same ~130 lines). `Schema`/`SchemaElement` carried no behaviour,
so every backend re-implemented the grammar. The duplication had already
drifted: a `src`→`title` mutation and a string-vs-native untyped result existed
in only one backend.

The fold now has exactly one home: `SchemaContentParser<TNode>` (a public deep
module implementing the unchanged `IContentParser`). A backend supplies only
four document-shaped primitives through a narrow public seam
`ISchemaBackend<TNode>`: `RootAsync`, `SelectMany`, `SelectOne`, `ExtractRaw`.
AngleSharp and JSON become ~25-line internal adapters. `AngleSharpContentParser`
/ `JsonContentParser` keep their exact `(ILogger)` constructors as thin shells.

Why the fold lives in `Core.Parser`, not on `Schema` in `Domain`: ADR 0001 set
the precedent that the *interpreter* lives in Core (`CrawlStep`) while the
domain type stays data (`Job`). Core already references AngleSharp, Newtonsoft
and `ILogger`, so the Core-placed fold emits `JObject` directly and logs
directly — **no new dependency anywhere**. Putting the fold on `Schema` would
have forced a parallel `ParseResult` union plus an `ISchemaLog` indirection into
Domain purely to keep it serializer-free; that ceremony is an artifact of
fighting the ADR-0001 precedent, not value.

The genuine seam is *what a leaf's raw value is*, not *how it is coerced*. The
typed-coercion switch (`DataType.Integer` → `int.Parse`, …) was byte-for-byte
identical across both backends once fed a string; it is now shared grammar in
the fold. The only real difference — AngleSharp's raw value is a `string`,
JSON's is a native `JToken` — rides entirely on `ExtractRaw`'s return type, so
the historical untyped divergence collapses to a one-line passthrough and is
preserved exactly (pinned by `ListParsingTests` vs `JsonParsingTests`, and by a
new explicit cross-backend test). The `src`→`title` mutation is quarantined
inside `AngleSharpSchemaBackend.ExtractRaw`, unreachable from the shared fold
and from any future backend.

`ILinkParser` is deliberately **not** expressed through `ISchemaBackend`. It has
no Schema, no tree, no recursion, and resolves hrefs against a base URL — a
single-adapter seam with a fake node type. Both independent designs reached this
conclusion; consistent with ADR 0001's rejection of indirection without
variation.

Intended, documented behaviour unifications (previously divergent by accident,
now uniform — see CONTEXT.md "Flagged ambiguities"): the missing-node log
message, the parsing-error log message, and the single-value path now requiring
a selector the same way the list paths always did. Observable outcomes (field
left empty / unset, parse not crashed) are unchanged; only the divergent log
text and the single-value selector-miss *mechanism* are made uniform.

Public surface is **additive** (`ISchemaBackend<TNode>`,
`SchemaContentParser<TNode>` in `WebReaper.Core.Parser`); fully
source-compatible. A new backend is now a ~25-line adapter reused against the
proven fold — that reach is the deepening's deliverable, so the seam is public,
not internal. Minor SemVer, in contrast to ADR 0001's major.

## Considered options

- **One Core fold + narrow public node-backend seam (chosen).** Typed coercion
  is shared grammar; raw-value shape is the per-backend thing. Total
  de-duplication including the coercion switch; no new dependency; quirks
  quarantined at `ExtractRaw`; the divergence becomes a typed property, not
  copied code.
- **Node-navigator port, everything internal (rejected).** Same shape but the
  seam stays `internal` and each adapter keeps its own copy of the
  typed-coercion switch. Leaves the real duplication (the switch) in place and
  delivers no reuse to consumers — a deepening only we benefit from.
- **De-anemic `Schema`: the fold on/with the Domain type (rejected).** The
  honest antithesis, but to keep Domain serializer-free it needs a parallel
  `ParseResult` union + `ISchemaLog` + a `ParseResult`→`JObject` mapper —
  surface and a translation pass that exist only to dodge a dependency Core
  already has. Contradicts the just-merged ADR-0001 placement precedent.
- **Fold `ILinkParser` into the same seam (rejected).** One real adapter, a
  fake node type, no tree to fold; indirection without variation.
