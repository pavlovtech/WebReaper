# `ISchemaInferrer` seam + `.ExtractInferred(goal?)` seed terminal + `LearnedSchemaContentExtractor` — runtime schema inference

## Status

**Accepted — design pass** (2026-05-25). Third (and final) ADR of the
v10.0.0 pre-tag AI-native slice. Pairs with the v10.0.0 cost-optimisation
slice (ADR-0065 + ADR-0066) — depends on `LlmCall<TResponse>` (ADR-0059)
and the telemetry seam (ADR-0066) but is otherwise independent. Closes
the firecrawl "extract structured data without a hand-authored schema"
parity gap. Folds into v10.0.0 — the tag waits on this PR.

## Context

The ADR-0025 staged builder gates "build with no extraction strategy"
through `ICrawlSeed`'s two terminals — `.Extract(schema)` (Schema-
driven structured extraction via the deterministic fold) and
`.AsMarkdown()` (no-schema LLM-ready Markdown via
`MarkdownContentExtractor`). Both terminals decide the strategy at
*seed-terminal time* — the schema is either supplied by the consumer
or absent.

Firecrawl, Browserbase Stagehand, and the rest of the AI-scraping cohort
ship a third strategy: **"extract structured data without a schema —
let the model figure out the shape from the page."** The consumer says
*what they want* (a natural-language goal like "product details" or
"job listings"), not *how to find it* (CSS selectors). The model
proposes a schema; the deterministic fold uses it. Once inferred, the
schema is cached for the rest of the crawl — first page pays the LLM,
every subsequent page runs the deterministic fold.

WebReaper has both halves of this pattern already:

- The **deterministic fold** (ADR-0002 / ADR-0039 `SchemaFold`) is the
  cheap-and-reliable per-page extractor — works perfectly given a
  Schema.
- The **LLM-as-proposer + deterministic-as-validator** wedge is the
  project-level invariant (ADR-0046 `ExtractionRouter`, ADR-0047
  `SelfHealingContentExtractor`, ADR-0050 `SemanticActCoordinator`,
  ADR-0051 `AgentEngine` — four docks of the same shape).

What's missing: the *schema-generation* application of the wedge.
The fourth dock has been there since the design pattern crystallised
(ADR-0046/0047/0050/0051); shipping it makes the pattern complete on
the extraction surface.

Three motivations:

- **Lowest-barrier-to-entry path.** A consumer who doesn't know CSS
  selectors gets structured data from `Crawl(url).ExtractInferred(goal:
  "product details")` — one line, zero selector knowledge. The
  scraper-of-last-resort posture: "if you can't write a Schema, the
  LLM writes it for you."
- **Source-gen on-ramp.** v2 can emit the inferred schema as a
  `[ScrapeSchema]` partial class — the consumer runs once, inspects
  the inferred schema, commits it as code, switches to
  `Crawl(url).Extract(MySchema.Schema)` for deterministic-forever-after.
  The runtime inferrer is the discovery tool; the source-gen emit is
  the freezer.
- **Pattern completion.** Four proposer-validator docks already exist
  in the codebase; the schema-inference dock is the only missing
  application on the extraction surface (ADR-0046 ⟶ post-fold
  validation; ADR-0047 ⟶ selector repair; ADR-0050 ⟶ action
  resolution; ADR-0051 ⟶ page selection; *this ADR* ⟶ schema
  generation). Shipping it makes the pattern axis explicit.

### What this ADR does and does not move

**Moves into core:**
- `ISchemaInferrer` seam (`WebReaper/Core/Parser/Abstract/`).
- `NullSchemaInferrer` sentinel (`WebReaper/Core/Parser/Concrete/`).
- `LearnedSchemaContentExtractor` core wrapper
  (`WebReaper/Core/Parser/Concrete/`) — implements `IContentExtractor`,
  wraps an `ISchemaInferrer` + an inner `IContentExtractor` (default
  `SchemaFold`), caches the inferred schema per-instance.
- `ICrawlSeed.ExtractInferred(string? goal = null)` — third strategy
  terminal.
- `CrawlSeed.ExtractInferred(...)` — implementation (marks the
  builder; deferred resolution at `BuildAsync`).
- `ScraperEngineBuilder.WithSchemaInferrer(ISchemaInferrer)` — the
  registration seam, sibling to `WithSchemaValidator` (ADR-0062).
- `ScraperEngineBuilder.BuildAsync` — resolves the marker: when
  `ExtractInferred` was used AND `_schemaInferrer` is still the null
  sentinel, throw `InvalidOperationException` with the actionable
  message ("call `.WithLlmSchemaInferrer(client)` or `.UseAi(client)`
  before BuildAsync"). Otherwise wrap the registered extractor with
  `LearnedSchemaContentExtractor`.

**Moves into satellite (`WebReaper.AI`):**
- `LlmSchemaInferrer` (`WebReaper.AI/LlmSchemaInferrer.cs`) —
  `ISchemaInferrer` impl. One `LlmCall<JsonObject>` (ADR-0059); same
  descriptor shape as the four existing `Llm*` adapters; reads
  `CachePolicy` (ADR-0065) and reports telemetry (ADR-0066).
- `LlmSchemaInferrerOptions`
  (`WebReaper.AI/LlmSchemaInferrerOptions.cs`) — per-role options
  record. Same shape as `LlmExtractorOptions`.
- `LlmSchemaInferrerRegistration.WithLlmSchemaInferrer(IChatClient,
  LlmSchemaInferrerOptions?)` — the satellite extension on
  `ScraperEngineBuilder`.

**Stays out of scope (v1):**
- **`.UseAi(...)` auto-wiring of the inferrer.** Recommended v1
  flow is **explicit** — `.ExtractInferred(goal).WithLlmSchemaInferrer(client)`
  (or via `.UseAi(client).WithLlmSchemaInferrer(client)`). Auto-wiring
  would entangle the inferrer with the existing extractor wiring
  (`.UseAi(Recommended)` wires `WithLlmFallback`, which conflicts
  semantically with `LearnedSchemaContentExtractor`). v2 may add an
  `AiPolicyMode.Inferred` arm or a `WireInferrer: true` flag on
  `AiOptions`.
- **Schema persistence across runs.** v1 caches the inferred schema
  on the `LearnedSchemaContentExtractor` instance — fresh engine,
  fresh inference. Persisting to disk / Redis / Mongo is a v2 question
  (and overlaps with the source-gen emit path).
- **Source-gen emit of the inferred schema.** v1 logs the inferred
  schema at Information level; the consumer copies it into a
  `[ScrapeSchema]` partial by hand. Automated emission via a Roslyn
  `ISourceGenerator` is a v2 question.
- **Nested schemas / lists-of-objects.** v1 inference produces
  single-level flat schemas (field name → CSS selector). Matches the
  ADR-0045 source-gen v1 constraint. v2 may widen to nested.
- **Multi-host crawls with one inferrer.** The cache is keyed by
  inferrer instance, not by host. A multi-host crawl where pages
  have different shapes will use the first-page-inferred schema for
  all hosts — caveat documented.
- **Goal as structured input.** v1 takes `string? goal`. A
  structured `InferenceGoal` record (with fields like
  `expectedRecordKind`, `preferList`, `excludeFields`) is a v2
  consideration.
- **Validator-driven re-inference.** v1: schema is inferred once
  and trusted. If subsequent pages fail validation (the
  `ISchemaValidator` ADR-0062 verdict), the run continues with the
  cached (possibly-stale) schema. v2 may add a re-inference trigger
  on N consecutive validation failures.

## Decision

Four pieces in core, three in the satellite. The `IChatClient` binding
stays in the satellite (ADR-0009 quarantine); the seam + the wrapper
live in core because they're not LLM-specific.

### 1. `ISchemaInferrer` seam (core)

`WebReaper/Core/Parser/Abstract/ISchemaInferrer.cs`. Public interface,
single method:

```csharp
namespace WebReaper.Core.Parser.Abstract;

/// <summary>
/// Proposes a <see cref="Schema"/> from a page's document content
/// (ADR-0067). Consumed by <see cref="LearnedSchemaContentExtractor"/>
/// when the consumer chose the <c>.ExtractInferred(goal?)</c> seed
/// terminal — the inferrer is called on the first page of the crawl;
/// the proposed schema is cached on the wrapper and reused for every
/// subsequent page (the LLM-as-proposer / deterministic-as-validator
/// wedge applied to schema generation).
/// </summary>
public interface ISchemaInferrer
{
    /// <summary>Infer a <see cref="Schema"/> from the document content.</summary>
    /// <param name="document">The page's content. May be raw HTML or
    /// pre-cleaned Markdown — the inferrer decides how to prepare it
    /// for the model.</param>
    /// <param name="goal">Optional natural-language hint about what
    /// to extract ("product details", "job listings", …). When null,
    /// the inferrer makes its best guess from the page content.</param>
    /// <param name="cancellationToken">Threaded to the underlying
    /// chat client.</param>
    /// <returns>A schema the deterministic fold can apply to this
    /// and subsequent pages.</returns>
    Task<Schema> InferAsync(
        string document,
        string? goal = null,
        CancellationToken cancellationToken = default);
}
```

### 2. `NullSchemaInferrer` sentinel (core)

`WebReaper/Core/Parser/Concrete/NullSchemaInferrer.cs`. Public:

```csharp
namespace WebReaper.Core.Parser.Concrete;

/// <summary>The default <see cref="ISchemaInferrer"/> sentinel —
/// throws on first call (ADR-0067). The builder's <c>BuildAsync</c>
/// detects this via reference identity and throws at build time when
/// <c>.ExtractInferred(...)</c> was used but no real inferrer was
/// registered. Throwing in <c>InferAsync</c> is the defence-in-depth
/// path for code that constructs a <see cref="LearnedSchemaContentExtractor"/>
/// directly with this sentinel.</summary>
public sealed class NullSchemaInferrer : ISchemaInferrer
{
    public static readonly NullSchemaInferrer Instance = new();
    private NullSchemaInferrer() { }

    public Task<Schema> InferAsync(
        string document,
        string? goal = null,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(
            "No ISchemaInferrer registered. Call .WithLlmSchemaInferrer(client) " +
            "(or .UseAi(client) then .WithLlmSchemaInferrer(client)) on the " +
            "ScraperEngineBuilder before BuildAsync().");
}
```

### 3. `LearnedSchemaContentExtractor` core wrapper

`WebReaper/Core/Parser/Concrete/LearnedSchemaContentExtractor.cs`.
Public:

```csharp
namespace WebReaper.Core.Parser.Concrete;

/// <summary>
/// <see cref="IContentExtractor"/> wrapper that infers a schema on
/// the first call and reuses it for the rest of the crawl
/// (ADR-0067). The LLM-as-proposer / deterministic-as-validator
/// wedge applied to schema generation:
/// <list type="bullet">
/// <item>First page: call <see cref="ISchemaInferrer.InferAsync"/>;
/// cache the result.</item>
/// <item>Subsequent pages: delegate to the inner
/// <see cref="IContentExtractor"/> (typically <c>SchemaFold</c>) with
/// the cached schema.</item>
/// </list>
/// Cache is per-instance — a fresh engine inference; resumed runs on
/// the same engine reuse. <see cref="SemaphoreSlim"/> guards the
/// double-checked-locking inference call so parallel first-page
/// workers don't race.
/// </summary>
public sealed class LearnedSchemaContentExtractor : IContentExtractor, IAsyncDisposable
{
    private readonly ISchemaInferrer _inferrer;
    private readonly IContentExtractor _inner;
    private readonly string? _goal;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lock = new(initialCount: 1, maxCount: 1);
    private Schema? _learned;

    /// <summary>Construct.</summary>
    /// <param name="inferrer">The schema inferrer (required).</param>
    /// <param name="inner">The inner extractor that consumes the
    /// inferred schema. Typically the default <c>SchemaFold</c>
    /// (ADR-0002 / ADR-0039).</param>
    /// <param name="goal">Optional natural-language hint passed to
    /// the inferrer.</param>
    /// <param name="logger">Optional logger; <see cref="NullLogger.Instance"/>
    /// when omitted.</param>
    public LearnedSchemaContentExtractor(
        ISchemaInferrer inferrer,
        IContentExtractor inner,
        string? goal = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(inferrer);
        ArgumentNullException.ThrowIfNull(inner);
        _inferrer = inferrer;
        _inner = inner;
        _goal = goal;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc/>
    public async Task<JsonObject?> ExtractAsync(string document, Schema? schema)
    {
        // The wrapped strategy: the inner extractor's per-call schema
        // is replaced by the inferred one. `schema` parameter is
        // ignored — the consumer chose ExtractInferred precisely
        // because they did NOT supply a schema.
        var learned = _learned;
        if (learned is null)
        {
            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_learned is null)
                {
                    _logger.LogInformation(
                        "LearnedSchemaContentExtractor: inferring schema (goal='{Goal}')",
                        _goal ?? "(none)");
                    _learned = await _inferrer.InferAsync(document, _goal).ConfigureAwait(false);
                    _logger.LogInformation(
                        "LearnedSchemaContentExtractor: inferred schema with {FieldCount} field(s)",
                        _learned.Children.Count);
                }
                learned = _learned;
            }
            finally
            {
                _lock.Release();
            }
        }
        return await _inner.ExtractAsync(document, learned).ConfigureAwait(false);
    }

    /// <summary>Expose the inferred schema for diagnostics / source-gen
    /// emit (v2 deferral). Returns <c>null</c> before the first
    /// extraction. Thread-safe (volatile read).</summary>
    public Schema? InferredSchema => Volatile.Read(ref _learned);

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _lock.Dispose();
        return ValueTask.CompletedTask;
    }
}
```

### 4. `ICrawlSeed.ExtractInferred(string? goal = null)` seed terminal

`WebReaper/Builders/ICrawlSeed.cs`. Append to the interface:

```csharp
/// <summary>
/// Choose runtime schema inference (ADR-0067): no <see cref="Schema"/>
/// supplied at build time; the registered <see cref="ISchemaInferrer"/>
/// proposes one from the first page's content, the deterministic fold
/// consumes it for every subsequent page. The LLM-as-proposer /
/// deterministic-as-validator wedge applied to schema generation —
/// the firecrawl-shaped "extract structured data without a schema"
/// path.
/// <para>
/// Requires an <see cref="ISchemaInferrer"/> registered via
/// <see cref="ScraperEngineBuilder.WithSchemaInferrer(ISchemaInferrer)"/>
/// or the satellite's
/// <c>WithLlmSchemaInferrer(IChatClient, LlmSchemaInferrerOptions?)</c>
/// extension before <see cref="ScraperEngineBuilder.BuildAsync"/>; the
/// build throws otherwise.
/// </para>
/// <para>
/// Example:
/// <code>
/// var engine = await ScraperEngineBuilder
///     .Crawl("https://shop.com/products")
///     .ExtractInferred(goal: "product details")
///     .WithLlmSchemaInferrer(chatClient)
///     .WriteToConsole()
///     .BuildAsync();
/// </code>
/// </para>
/// </summary>
/// <param name="goal">Optional natural-language hint about what to
/// extract. Threaded through to the inferrer; when null the inferrer
/// guesses from the page content.</param>
ScraperEngineBuilder ExtractInferred(string? goal = null);
```

### 5. `CrawlSeed.ExtractInferred(...)` implementation

`WebReaper/Builders/ScraperEngineBuilder.cs`, inside the
`CrawlSeed` inner class. Sibling to `Extract(schema)` /
`AsMarkdown()`:

```csharp
public ScraperEngineBuilder ExtractInferred(string? goal = null)
{
    // ADR-0067: mark the builder for deferred resolution at
    // BuildAsync time. The actual ISchemaInferrer is registered via
    // .WithSchemaInferrer(...) (or the satellite's
    // .WithLlmSchemaInferrer(...)); BuildAsync wraps the default
    // SchemaFold with LearnedSchemaContentExtractor when both the
    // marker and a real inferrer are present.
    _builder._inferenceMarker = new InferenceMarker(Goal: goal);
    return _builder;
}
```

A private record on the builder:

```csharp
private sealed record InferenceMarker(string? Goal);

private InferenceMarker? _inferenceMarker;
private ISchemaInferrer _schemaInferrer = NullSchemaInferrer.Instance;
```

### 6. `ScraperEngineBuilder.WithSchemaInferrer(...)` registration

`WebReaper/Builders/ScraperEngineBuilder.cs`. New public method:

```csharp
/// <summary>
/// Register an <see cref="ISchemaInferrer"/> (ADR-0067). The
/// inferrer is consumed only when the consumer chose the
/// <see cref="ICrawlSeed.ExtractInferred(string?)"/> seed terminal —
/// otherwise it is silently ignored (the deterministic fold uses the
/// hand-authored schema). For the LLM-backed inferrer, prefer the
/// satellite's
/// <c>WithLlmSchemaInferrer(IChatClient, LlmSchemaInferrerOptions?)</c>
/// extension.
/// </summary>
public ScraperEngineBuilder WithSchemaInferrer(ISchemaInferrer inferrer)
{
    ArgumentNullException.ThrowIfNull(inferrer);
    _schemaInferrer = inferrer;
    return this;
}
```

### 7. `BuildAsync` wiring — resolve marker, wrap extractor, throw on missing inferrer

Inside the existing `BuildAsync`, after `SpiderBuilder.Build()` but
before constructing `ScraperEngine`:

```csharp
// ADR-0067: resolve the inference marker if set. Wrap the
// SpiderBuilder's current content extractor (default SchemaFold or
// whatever the consumer registered) with LearnedSchemaContentExtractor.
if (_inferenceMarker is { } marker)
{
    if (ReferenceEquals(_schemaInferrer, NullSchemaInferrer.Instance))
    {
        throw new InvalidOperationException(
            ".ExtractInferred(...) was called but no ISchemaInferrer was registered. " +
            "Call .WithLlmSchemaInferrer(chatClient) on the builder before " +
            "BuildAsync(), or supply a custom ISchemaInferrer via " +
            ".WithSchemaInferrer(inferrer).");
    }
    var inner = SpiderBuilder.GetCurrentContentExtractor(); // tiny new internal accessor
    var wrapped = new LearnedSchemaContentExtractor(
        _schemaInferrer, inner, marker.Goal, Logger);
    SpiderBuilder.WithContentExtractor(wrapped);
    // ADR-0058: register the wrapper as a teardown hook so the
    // SemaphoreSlim disposes on engine teardown.
    OnTeardown(wrapped);
}
```

`SpiderBuilder.GetCurrentContentExtractor()` is one new internal
accessor that returns the registered (or default) extractor — read-
only.

### 8. `LlmSchemaInferrer` (satellite)

`WebReaper.AI/LlmSchemaInferrer.cs`. Sibling to `LlmContentExtractor`
/ `LlmSelectorRepairer` / `LlmActionResolver` / `LlmAgentBrain` —
fifth `Llm*` adapter, same descriptor pattern:

```csharp
public sealed class LlmSchemaInferrer : ISchemaInferrer
{
    private const string DefaultSystemPrompt =
        "You are inferring a CSS-selector-based extraction schema for a " +
        "web scraper. Look at the page content and the (optional) goal; " +
        "propose JSON of the form { \"fields\": { \"name\": \"selector\", " +
        "... } } where each field maps to a CSS selector that extracts " +
        "the named field's text. Pick stable selectors (prefer id over " +
        "class, class over tag; combine when needed for uniqueness). " +
        "Output only the JSON, no commentary, no Markdown fences.";

    private readonly LlmCall<JsonObject> _call;
    private readonly LlmSchemaInferrerOptions _options;

    public LlmSchemaInferrer(
        IChatClient chatClient,
        LlmSchemaInferrerOptions? options = null,
        ILlmCallTelemetry? telemetry = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _options = options ?? new LlmSchemaInferrerOptions();
        _call = new LlmCall<JsonObject>(chatClient, new LlmCallDescriptor<JsonObject>
        {
            Name = nameof(LlmSchemaInferrer),
            SystemPrompt = _options.SystemPrompt ?? DefaultSystemPrompt,
            BuildUserMessage = input => BuildUserPrompt((InferInput)input),
            ParseResponse = ParseInferredSchema,
            Model = _options.Model,
            Temperature = _options.Temperature,
            MaxResponseTokens = _options.MaxResponseTokens,
            SystemPromptCache = _options.CachePolicy ?? CachePolicy.Default,
        }, telemetry: telemetry);
    }

    public async Task<Schema> InferAsync(
        string document, string? goal = null, CancellationToken ct = default)
    {
        var content = _options.UseMarkdownPreClean
            ? HtmlToMarkdown.Convert(document)  // ADR-0063
            : document;
        var trimmed = content.Length > _options.MaxContentChars
            ? content[.._options.MaxContentChars] : content;
        var input = new InferInput(trimmed, goal);
        var result = await _call.InvokeAsync(input, ct);
        // ParseInferredSchema returned a JsonObject {"fields": {...}};
        // convert to Schema. Same single-level flat shape as
        // LlmAgentBrain's Extract arm (ADR-0060 §ParseDecisionTool).
        return BuildFlatSchema(result.Value);
    }

    // ... BuildUserPrompt / ParseInferredSchema / BuildFlatSchema helpers ...
}
```

### 9. `LlmSchemaInferrerOptions` (satellite)

`WebReaper.AI/LlmSchemaInferrerOptions.cs`. Mirror of
`LlmExtractorOptions` shape:

```csharp
public sealed record LlmSchemaInferrerOptions(
    string? Model = null,
    bool UseMarkdownPreClean = true,
    int MaxContentChars = 32_000,
    int MaxResponseTokens = 1024,
    float Temperature = 0.0f,
    string? SystemPrompt = null,
    CachePolicy? CachePolicy = null);
```

### 10. `WithLlmSchemaInferrer` registration (satellite)

`WebReaper.AI/LlmSchemaInferrerRegistration.cs`. Same shape as the
other satellite `WithLlm*` extensions (and threads telemetry via
`BuilderTelemetryExtensions` — ADR-0066):

```csharp
public static class LlmSchemaInferrerRegistration
{
    public static ScraperEngineBuilder WithLlmSchemaInferrer(
        this ScraperEngineBuilder builder,
        IChatClient chatClient,
        LlmSchemaInferrerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(chatClient);
        var telemetry = builder.GetOrCreateLlmTelemetry();
        return builder.WithSchemaInferrer(new LlmSchemaInferrer(chatClient, options, telemetry));
    }
}
```

### 11. Consumer-facing surface

```csharp
// Minimal — no .UseAi():
var engine = await ScraperEngineBuilder
    .Crawl("https://shop.com/products")
    .ExtractInferred(goal: "product details")
    .WithLlmSchemaInferrer(chatClient)
    .WriteToConsole()
    .BuildAsync();
await engine.RunAsync();

// With .UseAi() — the inferrer is wired separately (v1):
var engine = await ScraperEngineBuilder
    .Crawl("https://shop.com/products")
    .ExtractInferred(goal: "product details")
    .UseAi(chatClient)                        // wires caching + telemetry; doesn't auto-wire inferrer in v1
    .WithLlmSchemaInferrer(chatClient)
    .WriteToConsole()
    .BuildAsync();
```

## Considered options

### Fork 1 — `ISchemaInferrer` location: core seam vs satellite-only

| Option | What | Verdict |
|---|---|---|
| (a) Public seam in core; default impl in satellite | `ISchemaInferrer` in `WebReaper/Core/Parser/Abstract/`; `LlmSchemaInferrer` in `WebReaper.AI`. | **Recommended.** Symmetric with the four existing AI-adjacent seams (`IContentExtractor` ADR-0039, `ISelectorRepairer` ADR-0047, `IActionResolver` ADR-0050, `ISchemaValidator` ADR-0062 — all core seams with satellite implementations). Consumers can write deterministic / non-LLM inferrers (heuristic / cached / per-tenant) without taking an AI dep. |
| (b) Satellite-only — no core seam, just an `LlmSchemaInferrer` class | One satellite implementation. | Rejected. Closes the door on consumer-authored alternatives and breaks the established core-seam-per-AI-role symmetry. |
| (c) Static helper, no interface | `SchemaInference.InferAsync(client, doc, goal)` static. | Rejected. Can't be swapped at the builder seam; can't be wrapped by `LearnedSchemaContentExtractor`'s caching layer cleanly. |

### Fork 2 — Seed terminal signature

| Option | What | Verdict |
|---|---|---|
| (a) `ExtractInferred(string? goal = null)` | One optional string param. | **Recommended.** Mirrors firecrawl's natural-language "extract" param. Optional — the inferrer guesses from content when null. |
| (b) `ExtractInferred(ISchemaInferrer inferrer, string? goal = null)` | Pass the inferrer at seed-terminal time. | Rejected. Forces the consumer to construct the inferrer themselves; loses the satellite extension's nice `.WithLlmSchemaInferrer(client)` pattern. |
| (c) `ExtractInferred(InferenceOptions opts)` | Structured options bag. | Rejected (v1). Over-engineered for v1's single optional `goal` field. v2 may introduce a record if more knobs accumulate. |
| (d) `ExtractInferredFromLlm(IChatClient client, string? goal = null)` | Bundle satellite extension. | Rejected. Couples the seed terminal to the LLM satellite; breaks the core-seam / satellite-impl separation. Consumers wanting a deterministic inferrer (e.g. cached, per-tenant) would have no clean path. |

### Fork 3 — When inference happens

| Option | What | Verdict |
|---|---|---|
| (a) First page of the crawl (lazy, double-checked-locking) | The first `ExtractAsync` call infers; subsequent calls reuse the cached schema. | **Recommended.** Pays the LLM exactly once per engine; subsequent pages run pure deterministic fold (the hot path). `SemaphoreSlim`-guarded double-checked locking handles the `Parallel.ForEachAsync` race. |
| (b) At `BuildAsync` time | Fetch the start URL synchronously, infer the schema, bake into the config. | Rejected. Forces a real HTTP fetch at build time (consumers may pre-build offline); blocks `BuildAsync` on network. The lazy-first-page path is cleaner. |
| (c) Per-page inference, no caching | Every page gets its own freshly-inferred schema. | Rejected. Defeats the cost argument entirely; defeats the deterministic-as-validator wedge (every page paying the LLM). |

### Fork 4 — Cache lifecycle / identity

| Option | What | Verdict |
|---|---|---|
| (a) Per-`LearnedSchemaContentExtractor` instance | Cache lives on the wrapper. Fresh engine = fresh inference. Consecutive `RunAsync` calls on the same engine reuse. | **Recommended.** Matches the engine's natural lifecycle. Resumable agent / scraper runs on the same engine share the schema; new builder = fresh inference. |
| (b) Per-process (static) | One inferred schema per (URL, goal) tuple across the process. | Rejected. Conflicts with the per-engine telemetry / cache-reset pattern (ADR-0066 `LlmCallTelemetry.Reset` runs per `RunAsync`); cross-engine state leak. |
| (c) Persisted (file / Redis) | Survives process restart. | Rejected (v2). Useful but v2 — overlaps the source-gen-emit story; pick one or the other. |

### Fork 5 — Schema shape (flat vs nested)

| Option | What | Verdict |
|---|---|---|
| (a) Single-level flat schemas (field → selector) | `{ "title": "h1", "price": ".price" }`. | **Recommended (v1).** Matches the ADR-0045 source-gen v1 constraint. Sufficient for ~80% of structured-extraction pages. Easy to validate. |
| (b) Nested + lists | Full `Schema` JSON. | Rejected (v2). The LLM's nesting reliability degrades; validation is harder; v1 ships the common case. |
| (c) Free-form JSON Schema | The LLM proposes a JSON Schema; we build a `Schema` from it. | Rejected. Two layers of indirection; the JSON-Schema-to-`Schema` mapping is non-trivial. |

### Fork 6 — Goal parameter shape

| Option | What | Verdict |
|---|---|---|
| (a) Optional `string?` | "product details" / null. | **Recommended.** Matches firecrawl's API. Minimal surface area. |
| (b) Required `string` | Consumer always names the goal. | Rejected. The "infer from page content" path is valuable for exploratory scraping; requiring a goal forces guessing. |
| (c) Structured `InferenceGoal` record | `{ ExpectedRecordKind, PreferList, ExcludeFields }`. | Rejected (v1). Speculative generality; one optional string is the firecrawl-parity shape. v2 may widen if usage demands. |

### Fork 7 — `.UseAi(...)` auto-wiring of the inferrer

| Option | What | Verdict |
|---|---|---|
| (a) Don't auto-wire — explicit `.WithLlmSchemaInferrer(...)` required | The consumer composes `.ExtractInferred(...).UseAi(...).WithLlmSchemaInferrer(...)`. | **Recommended (v1).** `.UseAi(Recommended)` wires `WithLlmFallback` which conflicts semantically with `LearnedSchemaContentExtractor` (both replace the default extractor). v1 keeps them explicit; v2 may add `AiPolicyMode.Inferred` or a flag. |
| (b) Auto-wire when `ExtractInferred` was used | `.UseAi(...)` detects the marker and adds the LLM inferrer. | Rejected (v1). Conflicting wiring with `WithLlmFallback`; ordering-dependent (the seed terminal runs first, but `.UseAi(...)` only sees what's already on the builder); subtle. Defer to v2. |
| (c) Always auto-wire | `.UseAi(client)` always wires `WithLlmSchemaInferrer(client)`, regardless of seed terminal. | Rejected. Pays nothing for non-inference consumers (just one allocation), but pollutes the policy story; `.UseAi(...)` currently has a clean per-mode wiring matrix. |

### Fork 8 — Inferrer failure recovery

| Option | What | Verdict |
|---|---|---|
| (a) Throw on first-page inference failure | `LlmCallException` from the wrapper's `ExtractAsync`; the page-processor pipeline drops the page; the crawl continues with no learned schema; every subsequent first-page-attempt re-tries. | **Recommended (v1).** Matches the existing parse-retry behaviour at the `LlmCall` level. Logs at Error; consumer sees the failure in their sink output (no records). Simple. |
| (b) Cache a "null schema" sentinel | First failure caches a no-op; subsequent pages silently emit nothing. | Rejected. Silent failure mode; consumer sees zero records with no error trail. |
| (c) Bounded retry with backoff inside the wrapper | The wrapper retries the inferrer N times before propagating. | Rejected (v2). The `LlmCall` mechanism already does one parse-retry; adding another retry layer overcomplicates v1. v2 may add if the failure pattern is real. |

### Fork 9 — Per-page validation as re-inference trigger

| Option | What | Verdict |
|---|---|---|
| (a) Trust the inferred schema; never re-infer | Once cached, the schema is used forever (per engine). | **Recommended (v1).** Simple; matches the cost story (one inference per crawl). Pages with different structure produce empty results — visible in sink output, debuggable. |
| (b) Re-infer on N consecutive validation failures | The `ISchemaValidator` ADR-0062 verdict triggers re-inference. | Rejected (v2). Composes interestingly with ADR-0062; deferred until the failure pattern is observed in real consumers. |
| (c) Per-page inference (no cache) | Defeats the cost argument. | Rejected — covered by Fork 3. |

### Fork 10 — Source-gen emit of the inferred schema

| Option | What | Verdict |
|---|---|---|
| (a) Log the inferred schema; consumer copies by hand | v1's path. | **Recommended (v1).** The `LearnedSchemaContentExtractor` logs the inferred schema at Information after the first inference (`InferredSchema` property also accessible programmatically). Consumer pastes into a `[ScrapeSchema]` partial when they're ready to lock it. |
| (b) Roslyn `IIncrementalGenerator` emit at runtime | Automated emit to `Generated/SchemaName.cs`. | Rejected. Source generators run at build time, not runtime — would require a separate convention (runtime → file → next-build's source-gen). v2 question; overlaps with persistence (Fork 4 c). |
| (c) Helper extension to print the schema as `[ScrapeSchema]` code | `extractor.AsScrapeSchemaCode()` returns the C# string for paste. | Considered. Tiny utility, easy add. Defer to v1.1 — not load-bearing for v1. |

### Fork 11 — Inferrer signature: async return Schema vs return JsonObject

| Option | What | Verdict |
|---|---|---|
| (a) `Task<Schema>` — typed return | Consumer-readable; composable with the rest of the schema surface. | **Recommended.** The `LearnedSchemaContentExtractor` consumes a `Schema` directly. |
| (b) `Task<JsonObject>` — raw JSON | Lower-level. | Rejected. Forces every consumer to write the JSON-to-`Schema` mapping; that mapping is shared concern, belongs in the inferrer impl (or a static helper). |
| (c) `IAsyncEnumerable<SchemaProposal>` — streaming alternatives | The inferrer streams candidate schemas; the wrapper picks. | Rejected. Speculative; v1 ships one schema per inference. |

## Consequences

- **The "extract structured data without a schema" path exists.**
  Firecrawl-parity for the third strategy terminal. Lowest-barrier-to-
  entry consumer workflow: `Crawl(url).ExtractInferred(goal).WithLlmSchemaInferrer(client)`.
- **The proposer-validator pattern is complete on the extraction
  surface.** Five docks: ADR-0046 routing, ADR-0047 selector repair,
  ADR-0050 action resolution, ADR-0051 page selection, *this ADR*
  schema generation. The pattern is now an explicit axis of the
  library.
- **One inference per crawl; deterministic fold for the rest.** First
  page pays the LLM; every subsequent page runs the cheap path. Same
  cost discipline as ADR-0046/0047/0050.
- **No `.UseAi(...)` change.** v1 keeps the inferrer's wiring
  explicit; `.UseAi(...)` does not auto-wire it. Documented v1
  behaviour; v2 may add an `AiPolicyMode.Inferred` arm.
- **The fifth `Llm*` adapter.** `LlmSchemaInferrer` shares the
  ADR-0059 mechanism (`LlmCall<TResponse>` + descriptor); the four
  existing adapters' pattern extends to five with no friction.
  ADR-0065 caching applies (the inference call is a perfect cache-
  hit candidate when the same goal recurs across crawls). ADR-0066
  telemetry attributes the inference call to
  `nameof(LlmSchemaInferrer)`.
- **`BuildAsync` gains one check.** When `ExtractInferred` was used
  AND no real inferrer is registered, the build throws with an
  actionable message — same pattern as `AgentEngineBuilder`'s
  `BuildAsync` throwing on `NullAgentBrain`.
- **Single-host-crawl caveat.** The cache is per-engine, not per-
  host. Multi-host crawls where pages have different shapes will use
  the first-page-inferred schema everywhere. CLAUDE.md gotcha
  documents this.
- **CONTEXT.md** gains **Schema inferrer** + **Learned-schema
  content extractor** terms in the AI-native section, and a new
  relationship line linking the third seed terminal to the new seam.
- **CLAUDE.md** gets a one-line gotcha — `.ExtractInferred(goal?)`
  requires an `ISchemaInferrer` registered before `BuildAsync` or
  throws; the inferred schema is per-engine and trusted (no
  re-inference); single-host crawls assumed (cache is per-engine, not
  per-host).

## Bounded scope (v1)

- **No `.UseAi(...)` auto-wiring of the inferrer.** Fork 7 — v1
  keeps it explicit.
- **No schema persistence across runs / processes.** Fork 4 — v2
  question.
- **No source-gen emit.** Fork 10 — v2 deferral (manual paste from
  logs / `InferredSchema` property is the v1 path).
- **No nested schemas.** Fork 5 — single-level flat shape, matches
  ADR-0045 source-gen v1 constraint.
- **No structured goal type.** Fork 6 — optional string only.
- **No validator-driven re-inference.** Fork 9 — trust the
  inferred schema for the rest of the crawl.
- **No per-page inference / no streaming.** Forks 3, 11 — single
  inference per engine, single `Schema` return.

## Implementation (slice, when accepted)

**Core — three new files, two edits:**

1. **`WebReaper/Core/Parser/Abstract/ISchemaInferrer.cs`** — new
   public interface.
2. **`WebReaper/Core/Parser/Concrete/NullSchemaInferrer.cs`** — new
   public sentinel.
3. **`WebReaper/Core/Parser/Concrete/LearnedSchemaContentExtractor.cs`** —
   new public wrapper. `IAsyncDisposable` for the `SemaphoreSlim`.
4. **`WebReaper/Builders/ICrawlSeed.cs`** — append
   `ExtractInferred(string? goal = null)`.
5. **`WebReaper/Builders/ScraperEngineBuilder.cs`** — `CrawlSeed`
   inner-class gains `ExtractInferred` method; `ScraperEngineBuilder`
   gains `_inferenceMarker` field, `_schemaInferrer` field +
   `WithSchemaInferrer` public method; `BuildAsync` resolves the
   marker.
6. **`WebReaper/Builders/SpiderBuilder.cs`** — one new internal
   accessor `GetCurrentContentExtractor()` returning the registered
   (or default) extractor.

**Satellite — three new files:**

7. **`WebReaper.AI/LlmSchemaInferrer.cs`** — new public adapter.
8. **`WebReaper.AI/LlmSchemaInferrerOptions.cs`** — new public
   record.
9. **`WebReaper.AI/LlmSchemaInferrerRegistration.cs`** — new public
   static class with the `WithLlmSchemaInferrer` extension.

**Tests — `WebReaper.Tests/WebReaper.UnitTests/` + `WebReaper.AI.Tests/`:**

10. **`LearnedSchemaContentExtractorTests.cs`** (new, core) — pin
    the wrapper contract:
    - First call invokes the inferrer; subsequent calls don't.
    - Inferred schema is cached (`InferredSchema` property
      observable after the first call).
    - Parallel first-page calls under load — the
      `SemaphoreSlim` guards; the inferrer is called exactly once
      across N concurrent tasks.
    - Inner extractor receives the inferred schema (passed-`null`
      schema argument from the outer call is ignored — the inner
      sees the inferred).
    - `DisposeAsync` doesn't throw when called multiple times.
    - Throws when the inferrer is `NullSchemaInferrer.Instance` on
      first call (defence-in-depth).
11. **`ExtractInferredSeedTerminalTests.cs`** (new, core) —
    builder-level integration:
    - `Crawl(url).ExtractInferred()` returns a builder.
    - `Crawl(url).ExtractInferred().BuildAsync()` throws
      `InvalidOperationException` with the actionable message (no
      inferrer registered).
    - `Crawl(url).ExtractInferred().WithSchemaInferrer(stub).BuildAsync()`
      succeeds.
    - `Crawl(url).ExtractInferred(goal: "products").WithSchemaInferrer(stub).BuildAsync()`
      propagates the goal to the wrapper (via stub assertion).
    - `Crawl(url).ExtractInferred().WithContentExtractor(custom).WithSchemaInferrer(stub).BuildAsync()`
      wraps the custom extractor (not the default `SchemaFold`).
12. **`LlmSchemaInferrerTests.cs`** (new, satellite) — adapter
    contract:
    - Default system prompt + descriptor name pinned.
    - Stub `IChatClient` returns `{"fields":{"title":"h1"}}` —
      `InferAsync` returns a `Schema` with one `SchemaElement` named
      `"title"` with selector `"h1"`.
    - Goal threads into the user message when supplied.
    - `MarkdownPreClean` option controls the pre-clean (raw HTML
      vs Markdown).
    - `MaxContentChars` truncation honoured.
    - Telemetry reports under `nameof(LlmSchemaInferrer)`.
    - Caching policy default `Default` (à la carte; null in options
      → defaults to `Default` at the descriptor).
13. **`WithLlmSchemaInferrerTests.cs`** (new, satellite) —
    registration:
    - `.WithLlmSchemaInferrer(client)` sets the builder's
      schema inferrer (observable through
      `Crawl(url).ExtractInferred().WithLlmSchemaInferrer(client).BuildAsync()`
      succeeding).
    - Per-builder telemetry instance shared with other `WithLlm*`
      calls (one `LlmCallTelemetry` accumulator for the whole
      builder — same `ConditionalWeakTable` lookup).

**Docs:**

14. **CONTEXT.md** — AI-native section gains **Schema inferrer** +
    **Learned-schema content extractor** terms; relationship line
    on the third seed terminal + the proposer-validator pattern's
    fifth dock.
15. **CLAUDE.md** — section header updated `ADR-0040..0066` →
    `ADR-0040..0067`; one new gotcha bullet on
    `.ExtractInferred(...)` + the registration requirement +
    single-host-cache caveat + the cache-is-trusted (no
    re-inference) v1 behaviour.
16. **CHANGELOG.md** — new "10.0.0 — Schema inference (ADR-0067)"
    subsection alongside the cost-optimisation slice.

### Guardrails (when implemented)

- `dotnet build WebReaper.sln` — 0 errors.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — all
  existing tests pass; new `LearnedSchemaContentExtractorTests` +
  `ExtractInferredSeedTerminalTests` pass.
- `dotnet test WebReaper.Tests/WebReaper.AI.Tests` — all existing
  tests pass; new `LlmSchemaInferrerTests` +
  `WithLlmSchemaInferrerTests` pass.
- `dotnet publish WebReaper.AotSmokeTest -c Release` — native code
  generated; no IL-trim warnings. The core wrapper is AOT-clean
  (`SemaphoreSlim` + `Volatile.Read`); the satellite adapter sits
  outside the AOT boundary by design (ADR-0009).

## References

- ADR-0009 — registration seam + satellite pattern; the inferrer
  seam lives in core, the LLM implementation in `WebReaper.AI`.
- ADR-0025 — staged builder; `.ExtractInferred(...)` is the third
  strategy terminal on `ICrawlSeed`.
- ADR-0039 — `IContentExtractor` seam; the wrapper is one more
  adapter of the seam.
- ADR-0040 — `MarkdownContentExtractor` + `.AsMarkdown()` seed
  terminal; pattern reference for the seed-terminal-wires-an-
  extractor shape.
- ADR-0045 — source-gen; v2 path for "freeze the inferred schema
  as code."
- ADR-0046 — extraction router; first proposer-validator dock —
  *post-fold validation*.
- ADR-0047 — self-healing selectors; second dock — *selector
  repair*. The wrapper-with-cache shape this ADR mirrors.
- ADR-0050 — semantic page actions; third dock — *action
  resolution*. Same per-instance-cache lifecycle.
- ADR-0051 — agent driver; fourth dock — *page selection*.
- ADR-0059 — `LlmCall<TResponse>` mechanism; `LlmSchemaInferrer`
  is the fifth adapter consuming it.
- ADR-0062 — `ISchemaValidator` seam; v2 validator-driven
  re-inference (Fork 9 deferral).
- ADR-0063 — `HtmlToMarkdown` primitive; the satellite adapter's
  pre-clean uses it directly.
- ADR-0065 — `LlmCall<T>` system-prompt caching; `LlmSchemaInferrer`'s
  per-role `CachePolicy?` inherits via the satellite's
  `BuilderTelemetryExtensions` pattern.
- ADR-0066 — engine cost telemetry; the inference call reports as
  one `LlmCallUsage` per crawl (the cheapest dock — one call per
  engine).
