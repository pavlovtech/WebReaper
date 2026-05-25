# Validator-driven re-inference for `LearnedSchemaContentExtractor`

## Status

**Accepted — design pass** (2026-05-25). Second of two ADRs in the
v10.0.0 pre-tag AI-native completion wave. Closes ADR-0067 Fork 9 (the
validator-driven re-inference deferral). Pairs with ADR-0068
(`.UseAi(...)` auto-wiring). Depends on ADR-0062 (`ISchemaValidator`
seam). Folds into v10.0.0 — the tag waits on this PR.

## Context

ADR-0067 shipped the runtime-schema-inference path: first page pays the
LLM via `ISchemaInferrer.InferAsync`, the resulting `Schema` caches on
the `LearnedSchemaContentExtractor` wrapper, and every subsequent page
runs the deterministic fold against the cached schema. The v1
decision (Fork 9) was **trust the cached schema for the rest of the
crawl**:

> v1: schema is inferred once and trusted. If subsequent pages fail
> validation (the `ISchemaValidator` ADR-0062 verdict), the run
> continues with the cached (possibly-stale) schema. v2 may add a
> re-inference trigger on N consecutive validation failures.

The cost story justified the trust: one LLM call per crawl is the
dock's whole pitch. But the failure mode is silent — a wrong
first-page inference (an outlier page; a sparsely-populated example; an
A/B-tested template) leaves every subsequent page producing empty
records, with no recovery. The consumer sees "the inferrer ran; my
sink got zero records" and has to debug by hand.

WebReaper already has the validator (`ISchemaValidator`, ADR-0062 —
default `SchemaSatisfiedValidator`) consulted at three sites today —
the **Extraction router** (escalate to fallback), the **Self-healing
content extractor** (trigger repair), the **Agent driver** (surface as
`AgentDecisionOutcome.Failed`). Adding a fourth consumer — the
**Learned-schema content extractor** — extends the pattern to schema
generation: when N consecutive pages fail validation against the
cached inferred schema, drop the cache so the next call re-infers from
a fresh page.

Same LLM-as-proposer / deterministic-as-validator pattern as the four
existing docks; same per-instance cache lifecycle. The only new
machinery is the consecutive-failure counter + the cache-clear
trigger.

### What this ADR does and does not move

**Moves into core:**
- `LearnedSchemaContentExtractor` (ADR-0067) gains an optional
  `ISchemaValidator` constructor argument (default
  `SchemaSatisfiedValidator.Instance`), an
  `int reInferAfterFailures` argument (the threshold), and an
  `int maxReInferencesPerInstance` argument (the cost cap). The
  `ExtractAsync` body grows the post-inner-extract validation step
  + counter + cache-clear logic.
- `ScraperEngineBuilder.BuildAsync` wiring (ADR-0067) reads the
  builder-registered `ISchemaValidator` (`_schemaValidator`, the
  ADR-0062 field) and threads it into the wrapper alongside the
  inferrer + inner + goal.

**Moves into satellite (`WebReaper.AI`):**
- `LlmSchemaInferrerOptions` (ADR-0067) gains
  `int ReInferAfterFailures = 3` and
  `int MaxReInferencesPerInstance = int.MaxValue` fields.
- The satellite extension `WithLlmSchemaInferrer` threads these
  into the wrapper via the builder's options — actually NO: the
  options are the inferrer's, not the wrapper's; the wrapper reads
  the threshold from its own constructor argument. The satellite
  threads via a new builder method `WithSchemaInferenceTriggers` or
  via reading the satellite-side options at builder time. See
  Decision §3 for the chosen wiring.

**Stays out of scope (v1):**
- **Per-host re-inference triggers.** The cache is per-engine, not
  per-host (ADR-0067 v1 single-host caveat). Multi-host crawls with
  divergent shapes will trigger re-inference on the host boundary;
  v2 may add per-host counters and per-host cached schemas.
- **Asymmetric thresholds per field.** A schema with 10 fields where
  9 succeed and 1 always fails currently counts as 1 failure (the
  validator's "any required field empty" rule). v2 may add
  per-field counters for fine-grained repair.
- **Validator-driven re-inference + self-heal composition.** The
  self-heal wrapper (ADR-0047) and the learned-schema wrapper both
  consume the validator; combining them is a v2 question (see
  ADR-0068 Fork 3 for the analogous discussion).
- **Re-inference with a different goal.** v1 re-uses the original
  goal string. A v2 question — possibly with the validator's
  failure reason embedded in the goal.
- **Persistent re-inference history across runs.** The counter
  resets per-instance.

## Decision

One constructor-arg extension on the core wrapper; one options-record
field on the satellite; one builder-level wiring step that threads
the satellite's options through to the wrapper at `BuildAsync` time.

### 1. `LearnedSchemaContentExtractor` constructor extension (core)

`WebReaper/Core/Parser/Concrete/LearnedSchemaContentExtractor.cs`.
Append optional constructor arguments:

```csharp
public LearnedSchemaContentExtractor(
    ISchemaInferrer inferrer,
    IContentExtractor inner,
    string? goal = null,
    ILogger? logger = null,
    ISchemaValidator? validator = null,             // NEW (ADR-0069)
    int reInferAfterFailures = 0,                   // NEW (ADR-0069); 0 = never re-infer
    int maxReInferencesPerInstance = int.MaxValue)  // NEW (ADR-0069); cost cap
{
    ArgumentNullException.ThrowIfNull(inferrer);
    ArgumentNullException.ThrowIfNull(inner);
    ArgumentOutOfRangeException.ThrowIfNegative(reInferAfterFailures);
    ArgumentOutOfRangeException.ThrowIfNegative(maxReInferencesPerInstance);
    _inferrer = inferrer;
    _inner = inner;
    _goal = goal;
    _logger = logger ?? NullLogger.Instance;
    _validator = validator ?? SchemaSatisfiedValidator.Instance;       // NEW
    _reInferAfterFailures = reInferAfterFailures;                       // NEW
    _maxReInferences = maxReInferencesPerInstance;                      // NEW
}
```

The core default `reInferAfterFailures = 0` preserves the ADR-0067
v1 trust-the-cache behaviour for any consumer constructing the wrapper
directly without going through the satellite — same defensive-default
pattern as the satellite's `CachePolicy.Default` vs `Hinted` split.

### 2. `LearnedSchemaContentExtractor.ExtractAsync` body — validate + count + clear

The body grows three steps:

```csharp
public async Task<JsonObject> ExtractAsync(string document, Schema? schema)
{
    _ = schema;  // ADR-0067: ignored — the caller didn't supply one.

    var learned = Volatile.Read(ref _learned);
    if (learned is null)
    {
        // ADR-0067: SemaphoreSlim-guarded double-checked-locking
        // first-page inference (unchanged).
        await _lock.WaitAsync().ConfigureAwait(false);
        try { /* infer-and-cache; throw on null */ }
        finally { _lock.Release(); }
        learned = Volatile.Read(ref _learned)!;
    }

    var result = await _inner.ExtractAsync(document, learned).ConfigureAwait(false);

    // ADR-0069: validate; on consecutive failure, optionally drop the
    // cache to trigger re-inference on the next call.
    if (_reInferAfterFailures > 0)
    {
        var verdict = _validator.Validate(result, learned);
        if (verdict.IsValid)
        {
            // Reset the counter on any success — only *consecutive*
            // failures trigger re-inference.
            Volatile.Write(ref _consecutiveFailures, 0);
        }
        else
        {
            var failures = Interlocked.Increment(ref _consecutiveFailures);
            if (failures >= _reInferAfterFailures)
            {
                await TryDropCacheForReInferenceAsync(learned, verdict.Reason)
                    .ConfigureAwait(false);
            }
        }
    }

    return result;
}

private async Task TryDropCacheForReInferenceAsync(Schema staleSchema, string? reason)
{
    if (Interlocked.Increment(ref _reInferencesUsed) > _maxReInferences)
    {
        // Cost cap hit — log and leave the cache in place so the
        // run continues with the stale schema (the same failure
        // mode as v1; the cap is the consumer's "don't burn LLM
        // calls indefinitely on a bad host" guardrail).
        Interlocked.Decrement(ref _reInferencesUsed);  // undo the over-increment
        _logger.LogWarning(
            "LearnedSchemaContentExtractor: hit MaxReInferencesPerInstance ({Cap}); " +
            "keeping the stale schema (validator reason: '{Reason}'). " +
            "Records will continue to fail validation; consider raising " +
            "MaxReInferencesPerInstance or investigating the underlying cause.",
            _maxReInferences, reason ?? "(none)");
        return;
    }

    await _lock.WaitAsync().ConfigureAwait(false);
    try
    {
        // Only clear if the cached schema is still the one we observed
        // failing — another worker may have re-inferred between our
        // failure observation and acquiring the lock. Reference identity
        // is the safe check (Schema is a record but the wrapper only
        // ever swaps the field via Volatile.Write).
        if (ReferenceEquals(_learned, staleSchema))
        {
            _logger.LogInformation(
                "LearnedSchemaContentExtractor: dropping cached schema after " +
                "{Failures} consecutive validation failures (reason: '{Reason}'); " +
                "next call will re-infer (re-inferences used: {Used}/{Cap}).",
                _consecutiveFailures, reason ?? "(none)",
                _reInferencesUsed, _maxReInferences);
            Volatile.Write(ref _learned, null);
            Volatile.Write(ref _consecutiveFailures, 0);
        }
    }
    finally
    {
        _lock.Release();
    }
}
```

### 3. `ScraperEngineBuilder.BuildAsync` wiring (core)

The marker-resolution block in `BuildAsync` (ADR-0067) grows: read
the builder-registered `ISchemaValidator` (already there from
ADR-0062 via `_schemaValidator`) and thread it into the wrapper.
The thresholds come from the satellite — see §4. For the
core-builder path, defaults are read from new optional fields on the
marker:

```csharp
private sealed record InferenceMarker(
    string? Goal,
    int ReInferAfterFailures = 0,           // NEW (ADR-0069); 0 = preserve v1 trust
    int MaxReInferencesPerInstance = int.MaxValue); // NEW (ADR-0069)
```

The seed terminal stays as-is (`.ExtractInferred(string? goal = null)`)
— the consumer does not specify thresholds at the seed terminal; the
satellite or a separate builder method sets them. See §4.

```csharp
if (_inferenceMarker is { } marker)
{
    if (ReferenceEquals(_schemaInferrer, NullSchemaInferrer.Instance))
        throw new InvalidOperationException(/* ADR-0067 message — unchanged */);

    var inner = SpiderBuilder.GetContentExtractorOrDefault(Logger);
    var validator = _schemaValidator ?? SchemaSatisfiedValidator.Instance;
    var wrapped = new LearnedSchemaContentExtractor(
        _schemaInferrer, inner, marker.Goal, Logger,
        validator,                                  // NEW
        marker.ReInferAfterFailures,                // NEW
        marker.MaxReInferencesPerInstance);         // NEW
    SpiderBuilder.WithContentExtractor(wrapped);
    OnTeardown(wrapped);
}
```

### 4. Satellite wiring path — `LlmSchemaInferrerOptions` gains the threshold + cap

`WebReaper.AI/LlmSchemaInferrerOptions.cs` gains two fields:

```csharp
public sealed record LlmSchemaInferrerOptions(
    string? Model = null,
    bool UseMarkdownPreClean = true,
    int MaxContentChars = 32_000,
    int MaxResponseTokens = 1024,
    float Temperature = 0.0f,
    string? SystemPrompt = null,
    CachePolicy? CachePolicy = null,
    int ReInferAfterFailures = 3,                       // NEW (ADR-0069)
    int MaxReInferencesPerInstance = int.MaxValue);     // NEW (ADR-0069)
```

The **default** is `ReInferAfterFailures = 3` — the chosen opt-out
behaviour. The cost cap default is `int.MaxValue` (unbounded; the
consumer sets a real cap if they want one).

The satellite registration extension `WithLlmSchemaInferrer` (and via
`UseAi(Inferred)` in ADR-0068) reads these options and threads them
through. But the wrapper composition happens at `BuildAsync` time,
not at `WithLlmSchemaInferrer` time — by `BuildAsync` the satellite is
no longer in scope. Two paths to thread the values through:

**Path (a):** New builder method `WithSchemaInferenceTriggers(int
reInferAfterFailures, int maxReInferencesPerInstance)` that writes to
fields on the builder; `BuildAsync` reads them when populating the
marker. The satellite's `WithLlmSchemaInferrer` calls this method
internally with the option values; consumers can call it directly to
override.

**Path (b):** Marker fields populated from a new optional argument on
`.ExtractInferred(string? goal = null, int reInferAfterFailures = 0,
int maxReInferencesPerInstance = int.MaxValue)`. Two extra optional
args on the seed terminal.

**Verdict:** Path (a) — keep the seed terminal minimal (goal only;
matches firecrawl's API shape); add the builder method for explicit
override; satellite's `WithLlmSchemaInferrer` calls it.

```csharp
// ScraperEngineBuilder.cs additions:

public ScraperEngineBuilder WithSchemaInferenceTriggers(
    int reInferAfterFailures = 0,
    int maxReInferencesPerInstance = int.MaxValue)
{
    ArgumentOutOfRangeException.ThrowIfNegative(reInferAfterFailures);
    ArgumentOutOfRangeException.ThrowIfNegative(maxReInferencesPerInstance);
    _reInferAfterFailures = reInferAfterFailures;
    _maxReInferencesPerInstance = maxReInferencesPerInstance;
    return this;
}

private int _reInferAfterFailures;
private int _maxReInferencesPerInstance = int.MaxValue;
```

`BuildAsync` reads these into the marker's record fields when
composing the wrapper:

```csharp
var marker = _inferenceMarker with {
    ReInferAfterFailures = _reInferAfterFailures,
    MaxReInferencesPerInstance = _maxReInferencesPerInstance
};
```

(The marker stays a `record` with positional fields; the `with`
expression keeps the existing pattern.)

Satellite's `WithLlmSchemaInferrer` extension threads the option
values to the builder:

```csharp
public static ScraperEngineBuilder WithLlmSchemaInferrer(
    this ScraperEngineBuilder builder,
    IChatClient chatClient,
    LlmSchemaInferrerOptions? options = null)
{
    ArgumentNullException.ThrowIfNull(builder);
    ArgumentNullException.ThrowIfNull(chatClient);
    var opts = options ?? new LlmSchemaInferrerOptions();
    var telemetry = builder.GetOrCreateLlmTelemetry();
    builder.WithSchemaInferenceTriggers(
        opts.ReInferAfterFailures,
        opts.MaxReInferencesPerInstance);
    return builder.WithSchemaInferrer(new LlmSchemaInferrer(chatClient, opts, telemetry));
}
```

### 5. Consumer-facing surface

```csharp
// Default — auto re-infer after 3 consecutive failures, unbounded
// re-inferences per crawl:
var engine = await ScraperEngineBuilder
    .Crawl("https://shop.com/products")
    .ExtractInferred(goal: "product details")
    .WithLlmSchemaInferrer(chatClient)
    .WriteToConsole()
    .BuildAsync();

// Opt out — preserve v10.0.0 ADR-0067 trust-the-cache behaviour:
var engine = await ScraperEngineBuilder
    .Crawl("https://shop.com/products")
    .ExtractInferred(goal: "product details")
    .WithLlmSchemaInferrer(chatClient,
        new LlmSchemaInferrerOptions(ReInferAfterFailures: 0))
    .WriteToConsole()
    .BuildAsync();

// Cap re-inferences (e.g. cost guardrail for cron / unattended runs):
var engine = await ScraperEngineBuilder
    .Crawl("https://shop.com/products")
    .ExtractInferred(goal: "product details")
    .WithLlmSchemaInferrer(chatClient,
        new LlmSchemaInferrerOptions(
            ReInferAfterFailures: 3,
            MaxReInferencesPerInstance: 2))     // at most 2 re-inferences
    .WriteToConsole()
    .BuildAsync();

// Custom validator + custom inferrer (no satellite — bespoke):
var engine = await ScraperEngineBuilder
    .Crawl("https://shop.com/products")
    .ExtractInferred(goal: "product details")
    .WithSchemaInferrer(new HeuristicInferrer())
    .WithSchemaValidator(new ForceInvalidUntilNonEmptyValidator())
    .WithSchemaInferenceTriggers(reInferAfterFailures: 5)
    .BuildAsync();
```

## Considered options

### Fork 1 — Where the re-inference trigger lives

| Option | What | Verdict |
|---|---|---|
| (a) Inside `LearnedSchemaContentExtractor` — wrapper grows the validator + counter | The wrapper already owns the cached schema's lifecycle; the trigger co-locates. | **Recommended.** Single source of truth for the cache field; the cache-clear path stays inside the same lock that protects inference; no new wrapper to compose. |
| (b) New outer wrapper composing `LearnedSchemaContentExtractor` + validator | The outer wrapper validates the inner's output and signals re-inference via a new seam method on `LearnedSchemaContentExtractor` (e.g. `Invalidate()`). | Rejected. Two wrappers around the same schema cache; the `Invalidate()` seam method splits the cache lifecycle across two types; harder to reason about. |
| (c) Move the cache into a separate `ISchemaCache` seam shared by the wrapper and a new `ReInferringValidator` | Decouple cache from wrapper. | Rejected (v1). Over-engineering for one consumer; the cache is a tight-knit collaborator of the wrapper's lifecycle. v2 question if a second consumer of the cache appears. |

### Fork 2 — Default `ReInferAfterFailures` value

| Option | What | Verdict |
|---|---|---|
| (a) `0` — never re-infer; preserves ADR-0067 v1 behaviour exactly | Opt-in. Safest for existing consumers. | Rejected — *graded as the second-safest*. ADR-0067 acknowledged the silent-failure mode; shipping the deferral without flipping the default leaves the friction in place. The v10.0.0 cost is one extra LLM call mid-crawl on the rare bad-first-page case; the v10.0.0 benefit is records show up where they previously didn't. |
| (b) `3` — opt-out; auto-recover from a wrong first-page inference | The chosen default. Three consecutive failures is a strong signal the schema is wrong (one or two could be page-level outliers — an empty product, a sold-out listing). | **Recommended.** Cheap recovery; small behavioural delta for the rare bad-first-page case. Documented in the CHANGELOG as a v10.x-additive behaviour change (consumers can opt out via `0`). |
| (c) `1` — re-infer on the first failure | Aggressive; minimal latency on a wrong first-page inference. | Rejected. Over-triggers on legitimate outlier pages (a single empty listing). |

### Fork 3 — Cap on re-inferences per crawl

| Option | What | Verdict |
|---|---|---|
| (a) Unlimited (`int.MaxValue`) by default; consumer caps if they want | Lets a pathologically bad host re-infer every Nth failure; the per-call cost is one LLM call per cap hit. | **Recommended (default).** The cap is the consumer's cost guardrail; the library's default behaviour is "keep trying to recover". Logging at Warning on every re-inference is the audit trail. |
| (b) Capped at 1 by default | Single re-inference per crawl; second wrong schema = give up. | Rejected as default. Too conservative for the dock's whole-crawl span; the bad-host case is exactly what the cap-on-by-default would silently miss. Consumers wanting "exactly one retry" set `MaxReInferencesPerInstance: 1`. |
| (c) Capped at `N` per N pages crawled (sliding window) | Rate-limit re-inferences over time. | Rejected (v1). Sliding-window state across pages is more machinery than the dock's cost story justifies. v2 question if real-world thrashing appears. |

### Fork 4 — Counter reset semantics

| Option | What | Verdict |
|---|---|---|
| (a) Reset on any validation success (the chosen "consecutive failure" semantics) | Three failures must be in a row to trigger; any success between resets the counter. | **Recommended.** Standard "consecutive failure" semantics; matches user intuition; tolerates legitimate outlier pages without triggering. |
| (b) Cumulative failure counter (no reset) | After total N failures across the run, re-infer. | Rejected. The cumulative count grows with the crawl size; a 10 000-page crawl will hit 3 cumulative failures trivially regardless of schema quality. |
| (c) Per-host counter | Reset on host boundary. | Rejected (v1). Multi-host crawls are an ADR-0067 v2 question entirely; the single-host caveat covers it. |

### Fork 5 — Validator source

| Option | What | Verdict |
|---|---|---|
| (a) Builder-registered `ISchemaValidator` (the ADR-0062 seam — read at `BuildAsync` time) | Same source the other three docks consume. | **Recommended.** Symmetric with the existing pattern; consumers swapping the validator (e.g. for a force-invalid stub in tests) get the swap on every dock at once. The wrapper's optional constructor argument defaults to `SchemaSatisfiedValidator.Instance` for direct-construction callers. |
| (b) Per-wrapper validator (no builder integration) | The wrapper owns its own validator independently. | Rejected. Splits the validator surface — three docks read from the builder, one doesn't. Inconsistent. |

### Fork 6 — Goal on re-inference

| Option | What | Verdict |
|---|---|---|
| (a) Re-use the original goal | The consumer's intent hasn't changed; the inferrer gets the same hint. | **Recommended.** Simple; predictable; matches the consumer's mental model. |
| (b) Pass the validator's failure reason as additional context | Embed the validator's reason in a goal-extension string (e.g. `"product details (previous inference failed: required field 'price' empty on 3 consecutive pages)"`). | Rejected (v1). Composing the reason into the goal is interesting but speculative — the validator's reason is field-name-specific, not page-shape-specific; the inferrer might over-focus on the named field. v2 question. |

### Fork 7 — Re-inference + self-heal composition

| Option | What | Verdict |
|---|---|---|
| (a) v1 doesn't compose with self-heal | The two wrappers stack as siblings on the `IContentExtractor` seam; the order matters and v1 picks one. | **Recommended (v1).** Self-heal mutates an existing Schema's selectors; the learned-schema wrapper replaces the Schema entirely. Composing them changes the failure-mode semantics (does self-heal repair a learned-schema's selector, or does the wrapper re-infer the whole schema?). v2 question; ADR-0068 Fork 3 makes the parallel argument on the policy side. |
| (b) v1 composes self-heal as the inner extractor | The wrapper delegates to `SelfHealingContentExtractor(SchemaFold, repairer)`; failure triggers repair *and* counts toward re-inference. | Rejected (v1). Two recovery mechanisms running on the same failure; semantics undefined; harder to test. |

## Consequences

- **The silent-failure mode named in ADR-0067 has an automatic
  recovery path.** A wrong first-page inference no longer means "the
  rest of the crawl produces empty records" — three consecutive
  failures triggers a fresh inference. Consumers who want the strict
  v10.0.0 trust-the-cache behaviour set
  `LlmSchemaInferrerOptions(ReInferAfterFailures: 0)`.
- **The dock is now genuinely "first page pays the LLM, every
  subsequent page runs the fold" — until the fold says otherwise.**
  The validator becomes the gate the cache trusts; same shape as the
  four existing proposer-validator docks (ADR-0046 / 0047 / 0050 /
  0051), now with the fifth.
- **`LlmSchemaInferrerOptions` grows by two positional fields.**
  Pure additive; consumers using `new LlmSchemaInferrerOptions(...)`
  with named args see no break; consumers using positional
  construction past `CachePolicy` (rare) get a compile error and
  add the new fields.
- **`LearnedSchemaContentExtractor` constructor grows by three
  optional arguments.** Pure additive; direct-construction callers
  with no validator / no re-inference threshold see no behaviour
  change (defaults preserve v10.0.0).
- **Slight observable behaviour change with `.UseAi(...)` /
  `.WithLlmSchemaInferrer(...)` defaults**: a previously-bad crawl
  (zero records due to wrong first-page inference) may now produce
  records after one extra LLM call mid-crawl. Logged at Information
  on cache drop; Warning on cap hit. The CHANGELOG entry calls this
  out as additive behaviour.
- **CONTEXT.md** gains a relationship line linking the **Schema
  validator** (ADR-0062) to the **Learned-schema content extractor**
  (the fourth validator-consumer site).
- **CLAUDE.md** gets a one-line gotcha on the default re-inference
  threshold + cost cap.

## Bounded scope (v1)

- **No per-host re-inference triggers** (Fork 4 — v2 with the rest of
  the multi-host story).
- **No per-field counters** (Fork — out of scope; the validator's
  granularity is whole-schema verdict).
- **No self-heal + re-inference composition** (Fork 7 — v2).
- **No reason-embedded re-inference goals** (Fork 6 — v2).
- **No persistent re-inference history across runs.** Counter resets
  per-instance; consecutive runs on the same engine share the cache
  (and the counter).
- **No `MaxReInferences` per page count or per time window.** Just
  the absolute total cap.

## Implementation (slice, when accepted)

**Core — one new builder method, three edits:**

1. **`WebReaper/Core/Parser/Concrete/LearnedSchemaContentExtractor.cs`** —
   constructor extension (validator + threshold + cap optional args);
   private fields; `ExtractAsync` body grows the validate + count +
   conditionally-clear-cache block; new `TryDropCacheForReInferenceAsync`
   helper.
2. **`WebReaper/Builders/ScraperEngineBuilder.cs`** —
   - `InferenceMarker` record grows two fields (`ReInferAfterFailures`,
     `MaxReInferencesPerInstance`).
   - New `WithSchemaInferenceTriggers(int, int)` public method.
   - `BuildAsync` marker resolution reads the new fields + threads the
     builder's `_schemaValidator` into the wrapper.

**Satellite — one edit:**

3. **`WebReaper.AI/LlmSchemaInferrerOptions.cs`** —
   record grows two positional fields (`ReInferAfterFailures = 3`,
   `MaxReInferencesPerInstance = int.MaxValue`).
4. **`WebReaper.AI/LlmSchemaInferrerRegistration.cs`** —
   `WithLlmSchemaInferrer` calls `builder.WithSchemaInferenceTriggers(...)`
   before `WithSchemaInferrer(new LlmSchemaInferrer(...))`.

**Tests — `WebReaper.Tests/WebReaper.UnitTests/`:**

5. New `LearnedSchemaReInferenceTests.cs` (core, ~10 tests):
   - `ReInferAfterFailures = 0` preserves ADR-0067 v1 behaviour
     (no cache clear regardless of validator verdicts).
   - `ReInferAfterFailures = 3` clears the cache after 3 consecutive
     validation failures (verify via `InferredSchema` property
     transitioning non-null → null).
   - One success between failures resets the counter.
   - 4th call after cache-clear triggers `InferAsync` again (counter
     reset to 0; new schema cached).
   - Cap honoured: with `MaxReInferencesPerInstance = 1`, the second
     would-trigger event leaves the cache in place (the stale
     schema continues to be used) + logs Warning.
   - Validator default (`SchemaSatisfiedValidator.Instance`)
     correctly identifies empty string as invalid + integer 0 as
     valid (smoke check on the integration, not the validator
     itself).
   - Custom validator (`ForceInvalidValidator`) drives the trigger
     deterministically.
   - Parallel workers under cap: 16 parallel calls with 3-failure
     threshold and cap=1 trigger at most one re-inference total.
   - Re-inference uses the same goal.
   - `Validator = null` (constructor default) treated as
     `SchemaSatisfiedValidator.Instance` for v10-pre-0069 callers.

**Tests — `WebReaper.Tests/WebReaper.AI.Tests/`:**

6. New `LlmSchemaInferrerReInferenceOptionsTests.cs` (satellite, ~4 tests):
   - Default `LlmSchemaInferrerOptions().ReInferAfterFailures == 3`.
   - Default `LlmSchemaInferrerOptions().MaxReInferencesPerInstance == int.MaxValue`.
   - `WithLlmSchemaInferrer(client, options)` threads the option values
     to the builder's `WithSchemaInferenceTriggers` (verified via
     internal builder-state accessor).
   - Builder accessor: new internal property exposing the configured
     `(ReInferAfterFailures, MaxReInferencesPerInstance)` for the
     test.

**Docs:**

7. **CONTEXT.md** — **Schema validator** entry gains a fourth
   consumer-site line (the **Learned-schema content extractor**).
8. **CLAUDE.md** — section header `ADR-0040..0067` →
   `ADR-0040..0069` (paired with ADR-0068 doc commit); one new
   gotcha bullet on the re-inference default + the cost cap.
9. **CHANGELOG.md** — entry under the ADR-0068 + ADR-0069 wave
   subsection (one section heading for the pair) calling out the
   additive behaviour change in the default.

### Guardrails (when implemented)

- `dotnet build WebReaper.sln` — 0 errors.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — all existing
  tests pass; new `LearnedSchemaReInferenceTests` pass; existing
  `LearnedSchemaContentExtractorTests` still pass (the
  `ReInferAfterFailures = 0` default preserves their assertions).
- `dotnet test WebReaper.Tests/WebReaper.AI.Tests` — all existing
  tests pass; new `LlmSchemaInferrerReInferenceOptionsTests` pass.
- `dotnet publish WebReaper.AotSmokeTest -c Release` — native code
  generated; no IL-trim warnings (the wrapper additions are
  reflection-free; `SchemaSatisfiedValidator` is the ADR-0062
  default).

## References

- ADR-0046 — Extraction router; first consumer of the
  `ISchemaValidator` seam.
- ADR-0047 — Self-healing content extractor; second consumer;
  wrapper-with-cache lifecycle pattern shared with this ADR.
- ADR-0051 — Agent driver; third consumer (validator verdict ⟶
  `AgentDecisionOutcome.Failed`).
- ADR-0062 — `ISchemaValidator` seam; the validator this ADR
  consults becomes the fourth consumer.
- ADR-0067 — `LearnedSchemaContentExtractor`; Fork 9 (the v1
  deferral this ADR closes).
- ADR-0068 — `.UseAi(...)` auto-wiring; the sibling ADR in this
  wave.
