# `ContentExtractorPipeline` — the shared extractor + validator module both engine builders embed

## Status

**Accepted — implemented** (2026-05-28). Post-v10.0.1 architecture
deepening targeting the duplication between `ScraperEngineBuilder` and
`AgentEngineBuilder`. Both builders carried near-identical bodies for
the four extractor-cluster methods (`WithContentExtractor`,
`WithFallbackExtractor`, `WithSelfHealing`, `WithSchemaValidator`) —
the same "read current extractor or default, wrap with
`ExtractionRouter` / `SelfHealingContentExtractor`, read current
validator" logic written twice across the two outer-shell builders.
This ADR introduces an internal `ContentExtractorPipeline` module
both builders embed; the four methods become 1-line forwards.

## Context

ADR-0025 split the build path into two outer seams — the in-process
`ScraperEngineBuilder` (Crawl driver) and the `AgentEngineBuilder`
(Agent driver). "Two seams, not one bug" — the outer surfaces stay
distinct because the two drivers genuinely differ (Crawl is parallel
with a Spider per job; Agent is sequential with a decide → persist →
execute loop). The ADR-0025 verdict applies to the OUTER builders.

The INNER composition of the post-extraction surface is the same for
both drivers, though:

- A current `IContentExtractor` (the extractor the engine runs per
  page). ADR-0039 seam.
- A current `ISchemaValidator` (the verdict source for routing and
  self-heal). ADR-0062 seam.
- The wrappers that compose them: `ExtractionRouter` (ADR-0046
  primary-fallback) and `SelfHealingContentExtractor` (ADR-0047
  proposer-validator-cache).

Pre-refactor, the four `With*` methods that own this composition
lived twice — one copy per builder. Bodies were near-byte-identical:

- `WithContentExtractor(x)` — set the current extractor.
- `WithFallbackExtractor(fallback)` — `current = ExtractionRouter(current ?? default, fallback, validator, logger)`.
- `WithSelfHealing(repairer)` — `current = SelfHealingContentExtractor(current ?? default, repairer, validator, logger)`.
- `WithSchemaValidator(v)` — set the validator.

The wrapping methods read both the current extractor (with a
"current or default" fallback to the AngleSharp/CSS `SchemaFold`) and
the current validator (passed through to the wrapper or defaulted by
the wrapper). Validator state was a duplicated `_schemaValidator`
field on each builder. The default-extractor construction
(`new SchemaFold<IParentNode>(new AngleSharpSchemaBackend(), logger)`)
appeared in both builders' wrapper methods AND in their `BuildAsync`
methods AND in `SpiderBuilder.GetContentExtractorOrDefault`.

The deletion test:

- Delete `ScraperEngineBuilder.WithFallbackExtractor` — the complexity
  reappears in N callers (each consumer that wants LLM-fallback would
  hand-wire `ExtractionRouter`).
- Delete `AgentEngineBuilder.WithFallbackExtractor` — same answer.
- Delete the duplication of those two methods — the complexity
  concentrates in one place (the shared module). **The duplication
  was not earning its keep.**

## Decision

A new internal `ContentExtractorPipeline` (in `WebReaper/Builders/`)
owns the extractor + validator state and the four `With*` methods'
composition logic. Both builders embed one and forward.

### Module shape

```csharp
internal sealed class ContentExtractorPipeline
{
    private readonly Func<ILogger> _logger;       // a getter, not captured
    private IContentExtractor? _extractor;
    private ISchemaValidator? _validator;

    public ContentExtractorPipeline(Func<ILogger> loggerProvider);

    public IContentExtractor? Extractor { get; }   // null if none registered
    public ISchemaValidator? Validator { get; }    // null if none registered

    public void WithContentExtractor(IContentExtractor extractor);
    public void WithFallbackExtractor(IContentExtractor fallback);
    public void WithSelfHealing(ISelectorRepairer repairer);
    public void WithSchemaValidator(ISchemaValidator validator);

    public IContentExtractor GetExtractorOrDefault();  // current or new SchemaFold
}
```

Logger is captured via a `Func<ILogger>` getter, not a value, because
the outer builders mutate their `Logger` / `_logger` field via
`WithLogger` after the pipeline is constructed. The pipeline calls
the getter at each wrapper-construction site so the latest logger
flows through — same semantics as the pre-refactor code which read
the outer `Logger` property at call time.

### Builder integration

Each outer builder gains a `private readonly ContentExtractorPipeline _pipeline`
field, constructed in the builder's constructor with the logger
getter. Each builder's four `With*` methods become 1-line forwards:

```csharp
public ScraperEngineBuilder WithFallbackExtractor(IContentExtractor fallback)
{
    _pipeline.WithFallbackExtractor(fallback);
    return this;
}
```

The `_schemaValidator` field is removed from each builder; the
pipeline owns it.

`BuildAsync` reads the resolved extractor from the pipeline:
- `AgentEngineBuilder` passes `_pipeline.GetExtractorOrDefault()` to
  the `AgentEngine` constructor, and `_pipeline.Validator ?? SchemaSatisfiedValidator.Instance`
  as the validator.
- `ScraperEngineBuilder` syncs the pipeline's resolved extractor into
  `SpiderBuilder.WithContentExtractor(...)` immediately before
  `SpiderBuilder.Build()` (the Spider doesn't need to know about the
  pipeline; it gets the finalized extractor through the existing
  `SpiderBuilder` surface). The ADR-0067 `LearnedSchemaContentExtractor`
  wrapping code reads `_pipeline.Validator` and registers the wrapped
  extractor back through `_pipeline.WithContentExtractor(wrapped)`
  before the sync.

The seed terminals (`AsMarkdown()`, etc.) also register via the
pipeline (`Pipeline.WithContentExtractor(new MarkdownContentExtractor())`)
instead of bypassing it through `SpiderBuilder.WithContentExtractor`
directly — keeps a single source of truth for the current extractor.

### Scope decision: 4 methods only

The shared module covers the four methods where the composition
logic is non-trivial AND validator state is read across calls. It
does NOT cover:

- `AddSink` / `WriteToConsole` / `WriteToJsonFile` — 1-line list
  adders with no shared state or composition.
- `AddProcessor` — same shape.
- `WithActionResolver` — a 1-line field setter; the resolver isn't
  part of the extractor pipeline.
- `WithPageLoader` / `WithCookieStorage` / proxy / cookie methods —
  Crawl-specific or builder-specific; not duplicated symmetrically
  between the two builders.

For those methods the deletion test fails: pulling them into a
shared module would relocate complexity (each builder still needs a
1-line forward) without concentrating any composition logic. The
locality win is zero. Leave them.

### Naming

`ContentExtractorPipeline` over `ContentPipeline` / `PostExtractionPipeline` /
`ExtractionStack`:

- Names the **state and composition** of the content extractor, which
  is what the module owns. The validator is on it because the
  wrapping methods need it, not because it's the "validator stack."
- "Pipeline" matches CONTEXT.md's "page-processor pipeline" naming for
  the post-extraction surface, even though this module is just the
  extractor half.
- "ContentPipeline" was too broad (suggested it owned processors and
  sinks too, which it does not).
- "ExtractionStack" sounded like a deployment thing.

### SpiderBuilder stays unchanged

`SpiderBuilder.WithContentExtractor` and `Build()` are still needed
by `DistributedSpiderBuilder` (ADR-0009's reduced shell, which has
only `WithContentExtractor` and no wrapping methods — nothing to
share with `ScraperEngineBuilder`). The pipeline state lives on
`ScraperEngineBuilder._pipeline`; the resolved extractor is synced
into `SpiderBuilder` once at `BuildAsync` time.

## Considered options

### Fork 1 — Shape of the shared module

- **Embedded `ContentExtractorPipeline` component (chosen).** Each
  builder holds one; the four `With*` methods forward. Explicit
  composition, clear ownership, IDE-discoverable methods on each
  builder.
- **Default-interface-implementation mixin.** An interface with
  default-method implementations both builders satisfy. Rejected:
  C# default-interface-implementations are only accessible through
  the interface; the builders would still need explicit 1-line
  re-declarations to expose the methods on the type. Same boilerplate
  as the embedded shape, plus indirection.
- **Generic extension methods on an `IPipelineHost` marker
  interface.** `builder.WithFallbackExtractor(x)` resolves to an
  extension method that reads `builder.Pipeline.Extractor`. Loses
  IDE discoverability (the method is in a separate extensions
  namespace) and creates surprise when users navigate to definition.
- **Shared base class.** `class AgentEngineBuilder : BuilderBase`,
  `class ScraperEngineBuilder : BuilderBase`. Rejected: makes the two
  outer builders structurally related, which ADR-0025's "two seams,
  not one bug" framing argues against. The shared inner module is
  HAS-A, not IS-A.

### Fork 2 — Scope

- **Four extractor-cluster methods (chosen).** Where the composition
  logic is non-trivial.
- **All ~8 duplicated methods.** Includes sinks, processors,
  `WithActionResolver`, writer sugars. Rejected: those are 1-line
  list adds or property sets with no shared composition; the
  deletion test fails for them. Sharing would relocate complexity,
  not concentrate it.
- **Only the byte-identical methods.** Just `WithSchemaValidator`
  + the wrapper trio's near-identical bodies. Rejected: the wrappers
  read the validator from the validator-method's state, so
  `WithSchemaValidator` HAS to live with them.

### Fork 3 — Logger handling

- **`Func<ILogger>` getter (chosen).** Pipeline calls the getter at
  each wrapper-construction site. Captures `WithLogger` updates that
  happen between pipeline construction and the wrapper call —
  matches the pre-refactor "read Logger at the call site" semantics.
- **Captured `ILogger` value at construction time.** Rejected:
  silently breaks the case where `WithLogger` is called after the
  outer builder is constructed but before `WithFallbackExtractor`.
  Behaviour change.
- **`ILogger` setter the outer builder pushes on each `WithLogger`.**
  Functionally equivalent to the getter; the getter is cleaner
  (single source of truth: the outer builder's logger field).

### Fork 4 — Does SpiderBuilder absorb the pipeline?

- **Stays unchanged (chosen).** `DistributedSpiderBuilder` still
  needs `SpiderBuilder.WithContentExtractor`; nothing to share with
  `ScraperEngineBuilder` for that builder. Sync the pipeline's
  resolved extractor into SpiderBuilder once at `BuildAsync` time.
- **SpiderBuilder absorbs the pipeline.** Rejected: `SpiderBuilder`
  is the Spider's per-Job I/O shell (ADR-0022); owning extractor
  composition logic on top of its load-transport / proxy / cookie
  responsibilities would broaden it past the ADR-0022 shape.
- **`SpiderBuilder.WithContentExtractor` is removed; `Build()` takes
  the extractor as a parameter.** Rejected: would require
  `DistributedSpiderBuilder` to also hold its own pipeline (or
  duplicate the resolution logic), which doesn't earn its keep for
  a builder with one extractor-related method.

## Consequences

### Wins

- **Locality.** The composition logic for the extractor + validator
  cluster lives in one place. A future change (e.g. adding a new
  wrapper class) is one edit, not two.
- **Symmetry.** Both builders' `With*` method bodies are 1-line
  forwards. Adding a new extractor-cluster method (e.g.
  `WithCaching`) is one method on the pipeline plus a 3-line forward
  on each builder, with no risk of the bodies drifting.
- **The default-extractor invariant is in one place.** Pre-refactor
  the AngleSharp/CSS `SchemaFold` default appeared in three places
  (each builder's wrappers + `SpiderBuilder.GetContentExtractorOrDefault`
  + each builder's `BuildAsync`). Post-refactor it's in one
  (`ContentExtractorPipeline.GetExtractorOrDefault`).

### Costs

- **One more module.** The internal surface area grows by one class.
  Trade-off accepted for the locality + symmetry win.
- **Pipeline's logger is a `Func<>`.** A small indirection cost at
  each wrapper construction (one delegate invocation). Negligible at
  build time; the wrappers are constructed once per call, not per
  page.
- **`SpiderBuilder` keeps `WithContentExtractor` as an internal
  contract surface.** ScraperEngineBuilder.BuildAsync calls it once
  to sync the resolved extractor in. Modest coupling between the
  outer builder and SpiderBuilder, but no worse than the
  pre-refactor flow (which also called WithContentExtractor on
  SpiderBuilder).

### Non-changes

- ADR-0009 satellite quarantine intact. The pipeline lives in core
  (not the AI satellite); the AI satellite continues to register
  extractors via the existing builder surface, which transparently
  goes through the pipeline.
- ADR-0025 two-seam outer separation intact. The two outer builders
  stay distinct; only the inner shared module is named.
- AOT discipline preserved. The pipeline is a regular class with no
  reflection or runtime code-gen.
- Public API of both builders unchanged. The four `With*` methods
  retain their signatures and semantics; only the bodies forward
  instead of inlining the composition.

### Tests

- All 409 unit tests pass.
- All 240 AI tests pass (this branch is off `master`; the +13 tests
  from PR #133's `LlmToolArguments` ship with that branch).
- Behavior-preserving refactor; no test changes required.

## References

- ADR-0009 — registration seam + satellite pattern; the AI satellite
  still wires through the existing builder methods (now pipeline-backed).
- ADR-0022 — Crawl driver + Spider split; `SpiderBuilder` stays the
  Spider's per-Job shell, not absorbed.
- ADR-0025 — staged builder entry + two-seam pattern; this ADR
  complements rather than contradicts (the two outer seams hold; the
  shared inner module is named).
- ADR-0039 — `IContentExtractor` seam; what the pipeline composes.
- ADR-0046 — `ExtractionRouter`; the primary-fallback wrapper the
  pipeline's `WithFallbackExtractor` constructs.
- ADR-0047 — `SelfHealingContentExtractor`; the cached-patch wrapper
  the pipeline's `WithSelfHealing` constructs.
- ADR-0062 — `ISchemaValidator` seam; the validator state the
  pipeline owns and the wrappers consume.
- ADR-0067 — `LearnedSchemaContentExtractor`; reads
  `_pipeline.Validator` at BuildAsync time.
