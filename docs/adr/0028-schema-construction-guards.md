# Schema construction enforces its grammar at the add site; the fold's defensive checks become belt-and-suspenders

## Status

**Accepted — implementation complete** (2026-05-20; landed on branch
`adr-0028-schema-construction-guards` off `origin/master` 5c4e1d0,
awaiting merge — third slice of the fresh `/improve-codebase-architecture`
review wave, after ADR-0026 / PR #82 and ADR-0027 / PR #83).
Surface-additive (one new static factory `Schema.ListOf`) + one
narrowly-breaking behaviour change (a leaf or list-container added to
a Schema with no `Selector` now throws at the Add call instead of the
parse silently leaving the field unset). Folds into the 10.0.0 wave
the user is batching (already breaking via ADR-0025).

## Context

The **Schema** is the user-facing DSL — the very first thing every
consumer constructs. Its **interface** in the LANGUAGE.md sense
(*everything a caller must know*) is wider than its *types* suggest:
`Schema : SchemaElement, ICollection<SchemaElement>` lets you assemble
trees with collection-initialiser syntax, but the grammar rules — *a
list container needs a `Selector`*, *a leaf needs a `Selector` to
locate the value*, *a child must have a `Field`* — live nowhere on the
construction surface. They fire as exceptions inside the **Schema
fold** ([WebReaper/Core/Parser/Concrete/SchemaContentParser.cs:75-77](../../WebReaper/Core/Parser/Concrete/SchemaContentParser.cs#L75-L77)
for `Field is null`, [SchemaContentParser.cs:160-169](../../WebReaper/Core/Parser/Concrete/SchemaContentParser.cs#L160-L169)
for `RequireSelector`) only when the user crawls. A perfectly
invalid-looking Schema constructs cleanly and only blows up on first
contact with a page — and for a *leaf-list* it does not even blow up
visibly: the fold's per-leaf `try/catch (Exception)` ([SchemaContentParser.cs:99-106](../../WebReaper/Core/Parser/Concrete/SchemaContentParser.cs#L99-L106))
swallows the throw, logs at error level, and leaves the field unset.

The existing test `ListSchemaWithoutSelectorThrows`
([WebReaper.Tests/WebReaper.UnitTests/ListParsingTests.cs:80-92](../../WebReaper.Tests/WebReaper.UnitTests/ListParsingTests.cs#L80-L92))
documents the symptom: its name says *Throws*, its body asserts
`result["names"]` is `null` (because the throw was swallowed). Even
the test's author called this out as "thrown inside FillOutput's try
for leaf elements → logged, field left unset rather than crashing the
whole parse." That is the friction.

The asymmetry between leaves and containers is also load-bearing
hidden state. A *container* with `IsList = true` and no `Selector`
throws and aborts the parse (the container path is unguarded by the
catch); a *leaf* with the same defect quietly disappears. Two
near-identical user mistakes, two different outcomes, both at parse
time — and the user is told neither at the keystroke.

Three natural-but-broken shapes a user can write today:

```csharp
// (1) Leaf-list, no Selector — fold quietly drops the field.
new Schema { new SchemaElement("names") { IsList = true } }

// (2) Object-list, no Selector — fold throws mid-parse, aborts the page.
new Schema { new Schema("listings") { IsList = true, Children = { ... } } }

// (3) Non-list leaf, no Selector — fold swallows + logs + null field.
new Schema { new SchemaElement("name") }
```

Measured against LANGUAGE.md:

- **Locality is missing.** The construction interface (the part a user
  reads when authoring the DSL) does not name the rules; the fold (the
  *callee*) does. The bug class — invalid Schemas — has no test home at
  the construction interface.
- **The interface is not the test surface.** A SchemaElement with
  `Field = null` is unreachable from the public positional ctors but
  reachable in principle; the `Field is null` guard in the fold has
  *no* test exercising it from the public API. The guard is dead from
  the user's view and undead from the fold's view.
- **The natural shape is verbose and unenforced.** A list-of-objects
  requires the user to remember three things together — `IsList`,
  `Selector`, `Children` — none of which is a positional argument. The
  natural Schema shape has no name for "this is a list of objects."

The **deletion test** on the fold's defensive checks: if those checks
disappear and Add-site validation takes over, the construction surface
*reports* the bug at the keystroke and the fold reads cleaner. Users
get fast-fail; the fold gets a thinner contract; tests of "is my
Schema valid" become offline, no-document-needed.

## Decision

Move the Schema grammar's enforcement from the fold to the Add site,
and give the natural list-shape a name. Three additive changes plus
one belt-and-suspenders softening:

1. **`Schema.Add(SchemaElement)` validates at the add site.** Schema
   already overrides `ICollection<T>.Add` as the collection-initialiser
   entry point ([Schema.cs:33-36](../../WebReaper/Domain/Parsing/Schema.cs#L33-L36)).
   The override gains three rules that throw `ArgumentException` with
   a specific, actionable message:

   - **Field non-empty.** A child being added is a *named* field; an
     empty / whitespace / null `Field` means "no JSON output property,"
     which never matches what the user actually meant.
   - **Leaf needs a Selector.** A `SchemaElement` that is *not* a
     `Schema` (i.e., has no `Children` semantic) is a leaf — it must
     have a non-empty `Selector` to locate the value or value-list.
   - **List container needs a Selector.** A `Schema` with `IsList =
     true` must have a non-empty `Selector` to locate the list-item
     scope. A `Schema` with `IsList = false` (a nested non-list object)
     uses the parent scope and is *exempt* — it never reads its own
     Selector, by the fold's design.

   The validation throws at the `Add` call inside the
   collection-initialiser, so the throw site is the exact place the
   user wrote the bad element — best possible error location.

2. **`Schema.ListOf(string field, string selector, params SchemaElement[] children)`
   bundles the list-of-objects shape.** Today a user writes:

   ```csharp
   new Schema("listings") {
       Selector = ".card",
       IsList = true,
       Children = { … }
   }
   ```

   — four property-init lines, no construction-time enforcement that
   they belong together. The factory is one call that *cannot* omit
   the selector:

   ```csharp
   Schema.ListOf("listings", ".card",
       new SchemaElement("name", ".name"),
       new SchemaElement("price", ".price", DataType.Integer))
   ```

   It validates `field` non-empty and `selector` non-empty at the
   factory call (same exception family as `Add`), sets `IsList = true`,
   and seeds `Children`. Old shape keeps working (back-compat); the
   factory is the recommended path.

3. **The `ListSchemaWithoutSelectorThrows` test is updated.** It
   currently constructs a leaf-list with empty Selector and asserts
   the *silent* swallow-and-log behaviour. Post-0028 the construction
   throws — which is what the test name has always promised. The
   updated test asserts the throw at the Add call; a sibling test
   asserts the same for an object-list (the previously-divergent
   behaviour is now uniform — fast-fail in both arms).

4. **The fold's `Field is null` guard and the three `RequireSelector`
   calls become belt-and-suspenders.** They stay in
   `SchemaContentParser` but are renamed/commented as *invariant
   assertions* defending the one remaining path — *mutation after
   Add*:

   ```csharp
   var elem = new SchemaElement("name", ".name");
   var schema = new Schema { elem };
   elem.Selector = ""; // mutation after Add; not re-validated
   ```

   Records in this codebase use `{ get; set; }` (not `{ get; init; }`),
   so mutation-after-Add is mechanically possible. The fold's defences
   still catch it; they are no longer the *primary* line of defence.
   This is the deepening: the **interface is the test surface** for
   construction; the fold's checks are internal-consistency
   assertions, not user-facing failure modes.

The change is internal-only on the *structure* of the public surface
(no new types beyond the factory; no public-method removal); behaviour
on a malformed Schema changes from "silent or late throw at parse" to
"specific throw at the Add call." That is the breaking edge.

## Considered options

### (a) Replace the collection-initialiser DSL with a fluent builder

`SchemaBuilder.New().Leaf("title", ".text-3xl").Container("items", ".items", c => c.Leaf("name", ".name"))...Build()`.
Rejected: the current `new Schema { … }` syntax mirrors the JSON
output tree visually and is genuinely terse. A fluent builder is more
verbose without adding semantic power once Add-site validation is in
place. Replacing the DSL is a regression in readability with no
locality / leverage gain over option (1)+(2).

### (b) Make `SchemaElement` properties `init`-only (immutable after construction)

Closes the mutation-after-Add path completely; the fold's defensive
checks could be deleted outright. Rejected for this ADR's scope:
init-only properties on a record break any code that mutates
properties post-construction (e.g., a downstream consumer building a
Schema procedurally from a config file). The codebase shows no
in-repo mutation pattern, but external consumer impact is unknown and
the deepening's value is the *primary* construction-site enforcement
— belt-and-suspenders for the mutation-after-Add path is acceptable.
Init-only is a follow-up candidate, named in CONTEXT.md so future
reviews can revisit it.

### (c) Split the model: leaf vs container vs root as sibling types

`SchemaLeaf` / `SchemaObject` / `Schema` (root) as three sibling
record types instead of the current
`Schema : SchemaElement : ICollection<…>` inheritance. The
discriminator in the fold becomes the type, not `item is Schema`.
Rejected: a major refactor for limited locality gain. The current
`item is Schema` discriminator is one line and unambiguous; splitting
breaks the `Examples`-tested collection-initialiser shape
substantively. Worth revisiting only if (b) is also taken — together
they would make sense as a 12.0.0 model overhaul, well beyond the
fresh-review scope.

### (d) Internalise `SchemaElement()` parameterless ctor

Make the records' implicit parameterless ctor `internal`, so
`new SchemaElement { Field = "x" }` no longer compiles externally
(forcing the positional ctor). Rejected as bundled in this ADR:
not necessary once Add validates Field non-empty (the only damage
the parameterless ctor enables, namely `Field = null` on a child, is
caught at Add); the back-compat cost of internalising the default
ctor is real (any external user with the record-init shape breaks).
The Add-site validation is the right cut; the parameterless ctor's
remaining footprint is benign.

### (e) Keep the fold's defensive checks as primary, document the friction in CLAUDE.md

The lowest-effort "fix" — document the rules and trust users to read
docs. Rejected: documentation can't fail-fast at the keystroke.
LANGUAGE.md is explicit — *"the interface is the test surface"*; an
invariant a user can violate without their code failing locally is
not an interface.

## Consequences

- **Invalid Schemas fail fast at construction**, at the exact line the
  user wrote the bad element. Three pathological shapes (leaf-list
  with no Selector / object-list with no Selector / non-list leaf
  with no Selector) all throw `ArgumentException` with a specific
  message.
- **Behaviour parity restored** between leaf-list and object-list
  paths — both fail at construction now, instead of one silently
  dropping the field and the other aborting the parse.
- **The Schema DSL gains one name**: `Schema.ListOf(field, selector,
  …children)`. Users authoring a list-of-objects no longer have to
  remember the `IsList + Selector + Children` triple together.
- **The fold reads cleaner.** Defensive checks stay (belt-and-
  suspenders for the mutation-after-Add path) but the *primary*
  enforcement moves to construction. Future readers of
  `SchemaContentParser` see "the grammar is enforced upstream; this
  is internal consistency only."
- **Tests gain a construction-side surface.** A new
  `SchemaConstructionTests` (or expansion of `ListParsingTests`) pins
  the Add-site rules at the construction interface — offline,
  no-document-needed, no parser instantiated.
- **One existing test changes intent.**
  `ListSchemaWithoutSelectorThrows` now actually throws (at
  construction). Its body inverts from "assert silent unset" to
  "assert `Assert.Throws<ArgumentException>(…)`."
- **CONTEXT.md grows by one "Flagged ambiguities" bullet** capturing
  the decision and the four rejected paths so future reviews don't
  re-suggest them; the **Schema** glossary entry gets a one-line
  pointer at the construction-site invariants.
- **SemVer: a narrowly-breaking change.** Any external user
  constructing a pathological Schema today gets a new throw. The
  factory is purely additive. The breaking edge is contained to bad
  construction shapes — no working code regresses.

## Implementation status

All five planned changes landed in one commit on
`adr-0028-schema-construction-guards`:

1. ✅ `WebReaper/Domain/Parsing/Schema.cs` — `Add(SchemaElement)`
   validates with three rules (Field non-empty; leaf Selector non-empty;
   list-container Selector non-empty; non-list nested Schema exempt).
   `Schema.ListOf(string field, string selector, params SchemaElement[] children)`
   static factory bundles the list-of-objects triple, validates
   field+selector via `ArgumentException.ThrowIfNullOrWhiteSpace`, and
   adds each child via `Add` so the rules live in one home. The Schema
   record's remarks now document the construction-time invariants.
2. ✅ `WebReaper/Core/Parser/Concrete/SchemaContentParser.cs` — the
   `Field is null` guard in `FillOutput` and the three `RequireSelector`
   call sites carry comments naming them as ADR-0028 invariant
   assertions, kept as belt-and-suspenders for the mutation-after-Add
   path. No behaviour change in the fold.
3. ✅ `WebReaper.Tests/WebReaper.UnitTests/ListParsingTests.cs` — the
   `ListSchemaWithoutSelectorThrows` test now actually throws (renamed
   `ConstructingALeafListWithoutASelectorThrowsAtTheAddSite`), and a
   sibling `ConstructingAnObjectListWithoutASelectorAlsoThrowsAtTheAddSite`
   pins the now-uniform fast-fail for the object-list arm.
4. ✅ `WebReaper.Tests/WebReaper.UnitTests/SchemaConstructionTests.cs`
   (new) — 12 tests pinning every Add-site rule and the `Schema.ListOf`
   factory contract at the construction interface: `Add` rejects null,
   empty-Field leaf, empty-Selector leaf, empty-Selector leaf-list, and
   empty-Selector list container; `Add` accepts a well-formed leaf and
   a nested non-list Schema with no Selector (exempt by design);
   `ListOf` rejects empty field, empty selector, and bad children;
   `ListOf` composes naturally inside a root Schema's
   collection-initialiser.
5. ✅ `CONTEXT.md` — the **Schema** glossary entry gains a
   construction-site-invariants paragraph naming `Schema.ListOf`; one
   new "Flagged ambiguities" bullet captures the decision and the five
   rejected paths (fluent-builder DSL replacement / init-only
   properties / sibling-type model split / parameterless-ctor
   internalisation / documentation-not-enforcement) so future reviews
   don't re-suggest them.

### Guardrails (run on the branch tip)

- `dotnet build WebReaper.sln` — **0 errors, 17 warnings** (every
  warning pre-existing on `origin/master`; no new warning attributable
  to this ADR). `WarningsAsErrors=CS1591` on core therefore green: the
  new `Schema.ListOf` factory and the updated `Add` carry their XML
  documentation.
- `dotnet test WebReaper.sln --no-build` (non-Integration) — **126/126
  pass**: 107 unit (94 pre-0028 + 1 replaced + 1 new in
  `ListParsingTests` + 12 new in `SchemaConstructionTests`) + 10
  Sqlite + 4 Puppeteer + 3 Mongo + 1 Cosmos + 1 AzureServiceBus.
- `WebReaper.AotSmokeTest` — `dotnet publish -c Release` succeeds with
  no `IL2xxx`/`IL3xxx` warnings; the published native binary runs and
  prints `AOT SMOKE: ALL PASS` for all 8 round-trip cases. The Schema
  construction change is pure value-record API; AOT picture unchanged.
- Live-site `WebReaper.IntegrationTests` not run on the branch — they
  hit `alexpavlov.dev` with real Puppeteer/Chromium and `Task.Delay` up
  to 25 s, slow and environmentally flaky; the CI workflow runs them on
  the PR.
