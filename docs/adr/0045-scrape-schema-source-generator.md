# `[ScrapeSchema]` — a Roslyn source generator emitting `Schema` from attributed POCOs; the .NET-native differentiator

## Status

**Accepted — implementation complete** (2026-05-23; landed on branch
`ai-native-wave` off `origin/master`). **Slice 6 of the AI-native wave**
— [REPOSITIONING-PLAN.md §2.3](../REPOSITIONING-PLAN.md) calls
`[ScrapeSchema]` "the funnel's signature differentiator … that Pydantic+
instructor structurally cannot match." Additive — new generator
package + an attribute carrier. Folds into the unreleased 10.0.0 wave;
ships free, MIT.

## Context

The repositioning plan's §2.3 commits the source-generator wedge:

> Source-gen typed extraction `[ScrapeSchema]` … emits a `Schema`/
> `SchemaElement` tree and a reflection-free typed materializer +
> `System.Text.Json` `JsonSerializerContext`. The generator maps CLR
> types onto the fold's existing `DataType` coercion grammar
> (`SchemaContentParser.Coerce`) — reused, not re-derived (ADR-0002).

The wedge: Python's Pydantic + LLM extraction libraries (instructor /
ScrapeGraphAI / Firecrawl SDK) reach Pydantic ergonomics through
*runtime* reflection and type-introspection — which is exactly the
shape Python's late-binding + dynamic typing makes cheap. C# has a
structurally better answer: **compile-time** source generation.

A POCO like

```csharp
[ScrapeSchema]
public partial class Article
{
    [ScrapeField("h1")]
    public string? Title { get; set; }

    [ScrapeField(".views", Type = SchemaFieldType.Integer)]
    public int Views { get; set; }

    [ScrapeField(".tag", IsList = true)]
    public List<string> Tags { get; set; } = new();
}
```

emits — at compile time, zero reflection — a `static Schema Schema` and
a `static Article Materialize(JsonObject json)`. The caller chains
through the existing fluent surface:

```csharp
var engine = await ScraperEngineBuilder
    .Crawl("https://example.com")
    .Extract(Article.Schema)
    .Subscribe(parsed => HandleArticle(Article.Materialize(parsed.Data)))
    .BuildAsync();
```

The generated `Schema` feeds the existing ADR-0002 fold unchanged. The
generated `Materialize` is direct property assignment from the
`JsonObject` the sinks receive — no reflection, AOT-clean.

### Why the structural advantage is real

- **Pydantic** uses `__init__`-injected validators and runtime
  introspection — fast in CPython but reflection-equivalent for AOT
  scenarios. Pydantic in Mojo / CPython-AOT modes is still a research
  topic.
- **instructor** layers JSON-Schema generation + LLM round-tripping on
  Pydantic; the generation path is reflective.
- **C# source generators** produce strongly-typed, reflection-free,
  trim-friendly code at compile time. AOT-clean by construction.

That's the structural advantage REPOSITIONING-PLAN §2.3 cites; this
slice ships it.

### Scope cut (v1)

A real Roslyn source generator that handles the *common 80%*:

- Classes marked `[ScrapeSchema]` (must be `partial` so we can extend
  them).
- Properties marked `[ScrapeField("selector", Type = ..., IsList = ...,
  Attr = ...)]`.
- `Type` is the WebReaper `DataType` (inferred from the property type
  when unspecified — `int` → `Integer`, `string` → `String`, etc.).
- `IsList = true` paired with a `List<T>` / `T[]` property emits a
  `Schema` list of leaves *(of T)*.
- The generator emits one static partial method `Schema Schema { get; }`
  and a static `Materialize(JsonObject)` function.

**Deferred** — explicitly named, shape decisions parked for v2:

- **Nested `[ScrapeSchema]` types** (a property whose type is another
  attributed POCO → nested `Schema`). Doable in v2 by symbol-walking
  the `SemanticModel`; the v1 scope cut is single-level + lists of
  primitives.
- **List of nested POCOs** (`List<NestedItem>` where `NestedItem` is
  `[ScrapeSchema]`). Same as above — needs the nested-schema walker.
- **Records / `init` properties** — works for the common `{ get; set; }`
  shape; the more exotic `record` constructor binding lands in v2.
- **Polymorphic types / abstract base classes** — JSON polymorphism is
  ADR-0008-handled at the wire; v2 if a real caller surfaces.
- **JSON Schema emission** (for the LLM extractor ADR-0044) — that
  bridge lives in `SchemaJsonSchemaBridge`; the source generator's
  `Schema` flows through it unchanged.

The v1 scope cut is deliberate: ship the seam with real ergonomics, not
a Cadillac shape that hides the architecture. Same discipline as
ADR-0036 ("shape from the second adapter, not for it").

## Decision

Five moves; two new projects (one runtime, one analyzer).

### 1. `WebReaper.Extraction.Attributes` — the runtime attribute carrier

[WebReaper.Extraction.Attributes/](../../WebReaper.Extraction.Attributes/).
A tiny netstandard2.0 + net10.0 multi-targeted assembly containing
two attribute types and one enum:

- `[ScrapeSchema]` — marker on the class.
- `[ScrapeField(selector, ...)]` — marker on the property.
- `SchemaFieldType` enum — mirrors `WebReaper.Domain.Parsing.DataType`
  so consumer code doesn't import the domain (decoupling the public
  surface from the internal grammar).

Multi-targeted because the Roslyn analyzer needs netstandard2.0; the
runtime use needs net10.0. The two TFMs share the same attribute
class definitions.

### 2. `WebReaper.Extraction.Generators` — the Roslyn analyzer

[WebReaper.Extraction.Generators/](../../WebReaper.Extraction.Generators/).
netstandard2.0, references Microsoft.CodeAnalysis.CSharp, emits an
analyzer DLL the consumer's project picks up via
`<ProjectReference … OutputItemType="Analyzer" />`.

Implements `IIncrementalGenerator`. Pipeline:

1. `ForAttributeWithMetadataName("WebReaper.Extraction.Attributes.ScrapeSchemaAttribute")`
   to find every type carrying the marker.
2. For each, inspect properties; collect `ScrapeFieldAttribute` data
   (selector, type, is_list, attr).
3. Map CLR property types → `DataType` (when `Type` is unspecified on
   the attribute).
4. Emit a `partial class` extension containing:
   - `public static Schema Schema { get; } = ...;` — built at type-init.
   - `public static <T> Materialize(JsonObject json)` — direct
     property assignment, AOT-clean.

The generated file is added to the consumer's compilation; no runtime
asset.

### 3. Property-type → `DataType` inference

| CLR property type | Inferred `DataType` |
|---|---|
| `string` / `string?` | `String` |
| `int`, `int?`, `long`, `long?`, `short`, `short?`, `byte`, `byte?` | `Integer` |
| `float`, `float?`, `double`, `double?`, `decimal`, `decimal?` | `Float` |
| `bool`, `bool?` | `Boolean` |
| `DateTime`, `DateTime?`, `DateTimeOffset`, `DateTimeOffset?` | `DataTime` |
| `List<T>` / `T[]` (T is one of the above) | element type + `IsList = true` |
| Anything else | `null` (untyped pass-through), warning emitted |

Explicit `Type = SchemaFieldType.Integer` on the attribute overrides
inference. An unrecognised property type emits a non-fatal compile
warning (the user gets a string-typed leaf, the diagnostic tells them
to be explicit).

### 4. Materializer shape

```csharp
public static Article Materialize(System.Text.Json.Nodes.JsonObject json)
{
    var result = new Article();
    if (json["title"] is { } titleNode)
        result.Title = titleNode.GetValue<string>();
    if (json["views"] is { } viewsNode)
        result.Views = viewsNode.GetValue<int>();
    if (json["tags"] is System.Text.Json.Nodes.JsonArray tagsArray)
        result.Tags = tagsArray.Select(n => n!.GetValue<string>()).ToList();
    return result;
}
```

Each property is a single direct assignment from the JsonObject. No
reflection, no `Activator.CreateInstance`, no `JsonSerializer<T>`
reflection path. AOT-clean.

### 5. JSON Schema interop bridge — already exists

The LLM extractor's `SchemaJsonSchemaBridge` (ADR-0044) consumes any
`Schema`, including a source-generated one. No new bridge needed —
generator output and hand-written `Schema` are interchangeable, by
design.

## Considered options

### (a) A `[ScrapeSchema]` runtime reflection emitter — rejected

The plan §2.3 explicitly names compile-time source generation as the
structural advantage. A reflection emitter would invalidate the
positioning.

### (b) Use existing `[JsonPropertyName]` instead of a new attribute — rejected

`[JsonPropertyName]` describes JSON serialisation, not CSS selectors.
A `[ScrapeSchema]` POCO often needs both: the JSON property name (for
the materializer's JsonObject key) and the CSS selector (for the
fold). New attributes keep concerns separate.

### (c) Emit JSON Schema as well as `Schema` — rejected (deferred)

The LLM extractor already has `SchemaJsonSchemaBridge` which converts
on demand. Emitting twice would duplicate the source generator's
output for no caller benefit; the bridge runs once per crawl.

### (d) Support nested `[ScrapeSchema]` POCOs in v1 — rejected (deferred)

Genuinely useful but increases complexity ~4×. Symbol-walking the
`SemanticModel` for cross-type composition is solvable but adds:
* incremental-generator caching subtleties (the cache key must include
  the nested type's symbol; trivial in v1 with `EquatableArray<T>`,
  more involved with type composition);
* cycle detection;
* `Schema.ListOf` invocation generation.

v1 ships the common path (a POCO with primitive fields); v2 adds
nested support. The seam doesn't change.

### (e) Embed the attribute types in the generator and emit them — rejected

Emitting attribute types into the consumer's compilation is the
modern Roslyn pattern (e.g. `[GeneratedRegex]`). It works but adds
diagnostic noise (the consumer can't `using
WebReaper.Extraction.Attributes` cleanly during IntelliSense lag). A
multi-targeted attributes assembly is the more conservative shape —
attribute types are visible to the IDE the moment the package
reference resolves.

### (f) Make `Schema` a *generated property* on a partial method — rejected

```csharp
partial class Article
{
    public static partial Schema Schema { get; }
}
```

Required `partial` properties land in C# 13+ but are still rough. A
`public static readonly Schema Schema = …` (initialised at type-init)
is the boring, lift-anywhere shape.

## Consequences

- **The plan's signature differentiator ships.** Pydantic-parity that
  Python structurally cannot match — reflection-free, AOT-clean, IDE-
  visible, generated at compile time.
- **The fold (ADR-0002) is reused, not re-derived.** The generator
  emits a `Schema` exactly the SchemaFold understands; no new fold,
  no new coercion grammar.
- **The LLM extractor (ADR-0044) is composable.** A source-generated
  `Schema` flows through `SchemaJsonSchemaBridge` to JSON Schema for
  the LLM, then back to a `JsonObject` the source-generated
  `Materialize` projects to typed POCO. End-to-end typed extraction.
- **Two new packages, neither core-deep.** The attribute carrier is a
  tiny assembly; the generator is an analyzer (no runtime asset).
  Consumers add one `PackageReference … OutputItemType="Analyzer"`.
- **Bounded scope is explicit.** Nested `[ScrapeSchema]` types are
  deferred; the v1 ships the common case with named cuts so v2's
  shape isn't pre-decided.
- **CONTEXT.md** gains a **Source-gen schema** term + relationship
  line; CLAUDE.md gains a one-line gotcha (partial class required;
  property setters required).

## Implementation

Landed on `ai-native-wave`:

1. **`WebReaper.Extraction.Attributes/WebReaper.Extraction.Attributes.csproj`** —
   netstandard2.0 + net10.0 multi-target.
2. **`WebReaper.Extraction.Attributes/ScrapeSchemaAttribute.cs`**.
3. **`WebReaper.Extraction.Attributes/ScrapeFieldAttribute.cs`**.
4. **`WebReaper.Extraction.Attributes/SchemaFieldType.cs`** — mirror of
   `WebReaper.Domain.Parsing.DataType`.
5. **`WebReaper.Extraction.Generators/WebReaper.Extraction.Generators.csproj`** —
   analyzer project.
6. **`WebReaper.Extraction.Generators/ScrapeSchemaGenerator.cs`** —
   `IIncrementalGenerator` implementation.
7. **`WebReaper.Tests/WebReaper.Extraction.Generators.Tests/`** — tests
   that compile a POCO and assert the generated `Schema` + `Materialize`
   behave (via Roslyn's `CSharpGeneratorDriver`).
8. **CONTEXT.md** — term + relationship line.

### Guardrails (run on the branch tip)

- `dotnet build WebReaper.sln` — 0 errors.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — baseline passes.
- `dotnet test WebReaper.Tests/WebReaper.Extraction.Generators.Tests` —
  generator emits expected source; the emitted source compiles and
  the materializer works against a JsonObject.

## References

- ADR-0002 — Schema fold + node-backend seam; the fold the generator
  feeds.
- ADR-0008 — STJ-typed pipeline; the JsonObject the materializer
  consumes is that pipeline's terminal.
- ADR-0009 — registration-seam + satellite pattern; the generator is
  a satellite-of-a-different-kind (a build-time analyzer rather than a
  runtime package).
- ADR-0028 — Schema construction guards; the generator emits Schemas
  satisfying the same construction-time invariants
  (`Schema.ListOf(field, selector, …)` for lists).
- ADR-0036 — "shape from the second adapter, not for it"; cited for
  the v1 scope cut (nested types deferred).
- ADR-0044 — LLM extractor; consumes the generator's Schema via the
  same `SchemaJsonSchemaBridge`.
- REPOSITIONING-PLAN §2.3 — the locked source-generator decision this
  ADR cashes.
