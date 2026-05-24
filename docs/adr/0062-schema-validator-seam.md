# `ISchemaValidator` — promote the validator to a real seam

## Status

**Accepted — implemented** (2026-05-24). Fourth ADR of the post-AI-native-wave
deepening campaign. Composes with ADR-0061's `AgentDecisionOutcome`.
Promotes `SchemaSatisfiedValidator.IsSatisfied` from a static method
to a first-class seam — default implementation preserves the
ADR-0029 / ADR-0046 behaviour, consumers swap in "at least N
records," "LLM-validated semantic correctness," or any custom rule.
Completes the proposer-validator pattern's missing half — every
proposer the AI-native wave shipped has a deterministic validator
*concept*; only now does that validator become a *seam* sibling to
`IContentExtractor`. Folds into the same v10.x release.

## Context

The current code:

- `WebReaper/Core/Parser/Concrete/SchemaSatisfiedValidator.cs` — a
  public *static* class with `public static bool IsSatisfied(JsonObject
  result, Schema? schema)`.
- `ExtractionRouter` (ADR-0046) and `SelfHealingContentExtractor`
  (ADR-0047) both reference it directly: the router uses it as the
  default `isValid` predicate; the self-heal extractor calls it twice
  per page (once after the primary, once after the patched re-run).
- The router's constructor accepts a `Func<JsonObject, Schema?, bool>?`
  override but defaults to `SchemaSatisfiedValidator.IsSatisfied` — so
  *technically* it's a delegate seam, but only one caller (the router)
  consumes the override and the self-heal extractor doesn't even
  accept one.

The asymmetry is real:

| Caller | How it gets the validator | Can it be swapped? |
|---|---|---|
| `ExtractionRouter` | constructor `isValid` delegate, default to static method | yes (router-local) |
| `SelfHealingContentExtractor` | hardcoded `SchemaSatisfiedValidator.IsSatisfied(...)` | no |
| `LlmContentExtractor` | (does not validate; trusts the model) | — |
| (Future: ADR-0061's `Failed("validation: ...")` outcome on Extract) | (TBD; this ADR provides the seam) | — |

Three concrete cases the static-method shape can't serve:

1. **"At least N records were extracted."** A list-of-objects schema
   may declare `IsList=true`; the current validator accepts "non-empty
   list." A caller wanting "at least 5" has to wrap the router with a
   custom delegate. The self-heal extractor offers no such wrap point.
2. **"LLM-validated semantic correctness."** The proposer-validator
   pattern's natural fourth role: an LLM-backed validator that grades
   "does this extracted record actually answer the schema?" — distinct
   from the structural "are the fields present?" check. With a seam,
   this is one `LlmSchemaValidator` adapter (ADR-0059's `LlmCall<T>`
   makes it ~30 lines). Without a seam, every caller re-implements.
3. **"Schema-validated against ADR-0061's outcome."** The agent driver
   needs to know "did the Extract satisfy the schema?" so it can
   emit `Failed("validation: <reason>")` instead of `Extracted(...)`
   on a structurally-empty record. The current static method returns
   `bool`; the agent needs a *reason* for the brain to read.

The static-method shape was OK at the ADR-0046 timeframe when there
was one caller. With two callers (router + self-heal), one new caller
(agent engine, this ADR + ADR-0061), and one anticipated LLM
validator (ADR-0059's mechanism), it's the small-but-real seam
ADR-0036 names *"shape from the second adapter, not for it"* — the
second-adapter has arrived.

### What the seam returns

`bool` is the current shape. The natural extensions:

- **The caller wants a reason.** The self-heal repairer is asked
  *which fields* failed — currently it pulls the failed result and
  scans for empty values to know what to patch. With a structured
  reason, the validator can name them once and the repairer reads
  them.
- **The agent driver wants a one-liner for `Failed.Reason`** —
  e.g., "validation: 'title' empty; 'price' missing." Free-form
  string; not parsed by the agent, just shown to the brain.

The shape:

```csharp
public sealed record ValidationResult(bool IsValid, string? Reason);
```

`Reason` is null when `IsValid == true`. When `IsValid == false`,
`Reason` is a human-readable summary the next step in the pipeline
can consume.

### Where the validator runs

Three sites in the pipeline:

1. **Inside `ExtractionRouter`** — between primary and fallback.
   Unchanged shape; the predicate becomes an `ISchemaValidator`.
2. **Inside `SelfHealingContentExtractor`** — between the primary
   pass and the repair invocation, and between the repaired pass and
   the cache decision. Two call sites; both swap from static method
   to the injected `ISchemaValidator`.
3. **Inside `AgentEngine`** — after every `Extract` decision's
   content extraction. If invalid, the engine populates
   `AgentDecisionOutcome.Failed("validation: <reason>", null)`
   (ADR-0061 composition); if valid, the engine populates
   `Extracted(...)` and proceeds to processors / sinks.

The third site is the load-bearing composition with ADR-0061 —
without the seam, the agent's "did the schema validate?" question
is answered by static-method-call from the engine; with the seam,
the consumer can swap the policy from inside `.UseAi(...)` or from
the agent builder directly.

### Where the validator does *not* run

- **Inside `LlmContentExtractor`.** The LLM extractor trusts its
  model; validation is a separate pipeline concern (the router uses
  the LLM extractor as a *fallback* — running validator on the
  fallback's output would re-loop). The router's job is "primary
  output failed validation → fallback"; the fallback is the
  authoritative output, not re-validated by the router. ADR-0044's
  bounded scope stands.
- **Inside `IPageProcessor`** as a separate processor. A processor
  runs *after* the extractor; the validator must run *during* the
  router / self-heal composition to influence the routing decision.
  A "schema validator processor" is a different concept (a sink-side
  filter) and is out of scope here.

## Decision

Four pieces — one new seam, one renamed-and-refactored default
implementation, three wire-up changes, one new builder method.

### 1. `ISchemaValidator` — the new seam

`WebReaper/Core/Parser/Abstract/ISchemaValidator.cs`. Public interface,
one method:

```csharp
public interface ISchemaValidator
{
    /// <summary>
    /// Validate <paramref name="extracted"/> against <paramref name="schema"/>.
    /// Returns <see cref="ValidationResult.IsValid"/> = <c>true</c> when the
    /// extraction satisfies the schema for this validator's policy; otherwise
    /// <c>false</c> with a populated <see cref="ValidationResult.Reason"/>.
    /// A null <paramref name="schema"/> is the strategy-local "no schema
    /// available" case — the default validator treats it as trivially valid;
    /// custom validators may treat it differently.
    /// </summary>
    ValidationResult Validate(JsonObject? extracted, Schema? schema);
}

public sealed record ValidationResult(bool IsValid, string? Reason)
{
    public static ValidationResult Valid { get; } = new(true, null);
    public static ValidationResult Invalid(string reason) => new(false, reason);
}
```

### 2. `SchemaSatisfiedValidator` becomes the default `ISchemaValidator`

The existing `SchemaSatisfiedValidator` (currently a static class) is
refactored to *implement* `ISchemaValidator` as an instance class
while *also* keeping a backward-compatible static `IsSatisfied`
method delegating to the instance for the v10.x cycle (deletion in
v11). The instance is `WebReaper.Core.Parser.Concrete.SchemaSatisfiedValidator`,
unchanged location and namespace:

```csharp
public sealed class SchemaSatisfiedValidator : ISchemaValidator
{
    public static SchemaSatisfiedValidator Instance { get; } = new();

    public ValidationResult Validate(JsonObject? extracted, Schema? schema)
    {
        if (schema is null || extracted is null) return ValidationResult.Valid;

        var failures = new List<string>();
        foreach (var child in schema.Children)
            CheckElement(extracted, child, path: "", failures);
        return failures.Count == 0
            ? ValidationResult.Valid
            : ValidationResult.Invalid("missing or empty: " + string.Join(", ", failures));
    }

    private static void CheckElement(JsonObject parent, SchemaElement element, string path, List<string> failures)
    {
        // ... existing IsSatisfied per-arm rules, but accumulating failure
        //     paths ("price", "items[].name", ...) instead of bool-shorting.
    }

    /// <summary>Backward-compatible static form. Marked obsolete; prefer
    /// <see cref="Validate"/> via the seam. Removed in v11.</summary>
    [Obsolete("Use ISchemaValidator.Validate via WithSchemaValidator. Removed in v11.")]
    public static bool IsSatisfied(JsonObject result, Schema? schema)
        => Instance.Validate(result, schema).IsValid;
}
```

The per-element rules (ADR-0029 alignment — integer 0 / boolean false
are valid; only string-empty / list-empty triggers; container
presence is sufficient when non-list) preserved exactly; the only
behaviour change is the *Reason* now carries the path to each failed
field.

### 3. `ExtractionRouter` takes `ISchemaValidator`

```csharp
public sealed class ExtractionRouter : IContentExtractor
{
    public ExtractionRouter(
        IContentExtractor primary,
        IContentExtractor fallback,
        ISchemaValidator? validator = null,
        ILogger? logger = null)
    {
        // _validator = validator ?? SchemaSatisfiedValidator.Instance;
    }

    public async Task<JsonObject> ExtractAsync(string document, Schema? schema)
    {
        var primaryResult = await _primary.ExtractAsync(document, schema);
        var verdict = _validator.Validate(primaryResult, schema);
        if (verdict.IsValid)
        {
            _logger.LogInformation("Router: primary valid; no fallback.");
            return primaryResult;
        }
        _logger.LogInformation("Router: primary invalid ({Reason}); fallback.", verdict.Reason);
        return await _fallback.ExtractAsync(document, schema);
    }
}
```

The `Func<JsonObject, Schema?, bool>?` constructor parameter is
removed (breaking; v10.x major posture aligned with ADR-0060). A
consumer with a custom predicate either:
- implements `ISchemaValidator` directly (one method);
- wraps in a small adapter `class DelegateSchemaValidator(Func<...> f) : ISchemaValidator`
  if they prefer the Func style (this adapter is *not* shipped in
  core — keep it internal to the consumer codebase).

### 4. `SelfHealingContentExtractor` takes `ISchemaValidator`

```csharp
public sealed class SelfHealingContentExtractor : IContentExtractor
{
    public SelfHealingContentExtractor(
        IContentExtractor primary,
        ISelectorRepairer repairer,
        ISchemaValidator? validator = null,
        ILogger? logger = null)
    {
        // _validator = validator ?? SchemaSatisfiedValidator.Instance;
    }
    // ... uses _validator.Validate(...) at both call sites.
}
```

The repairer's `RepairAsync` signature now optionally receives the
`ValidationResult.Reason` (a string the LLM repairer can use directly:
"the failed fields were 'title', 'price'"). The change is additive on
`ISelectorRepairer`:

```csharp
public interface ISelectorRepairer
{
    Task<Schema?> RepairAsync(
        Schema original,
        string document,
        JsonObject failedResult,
        string? failureReason,         // NEW — ADR-0062
        CancellationToken cancellationToken = default);
}
```

Default repairer (`LlmSelectorRepairer`) reads `failureReason` and
injects it into the prompt: "The validator reported: <reason>. Propose
selectors for those fields."

### 5. `AgentEngine` consults `ISchemaValidator` after each `Extract`

Engine constructor gains the validator (default
`SchemaSatisfiedValidator.Instance`). In the `Extract` switch arm:

```csharp
case AgentDecision.Extract extract:
    try
    {
        var extracted = await _contentExtractor.ExtractAsync(pageHtml, extract.Schema);
        var verdict = _validator.Validate(extracted, extract.Schema);
        if (!verdict.IsValid)
        {
            lastOutcome = new AgentDecisionOutcome.Failed(
                Reason: $"validation: {verdict.Reason}",
                ExceptionType: null);
            step++;
            break;
        }
        var processed = await RunProcessorsAsync(...);
        // ... ADR-0061 composition: Extracted outcome populated on success
    }
    catch (Exception ex)
    {
        lastOutcome = new AgentDecisionOutcome.Failed(...);
    }
    step++;
    break;
```

This is the **real composition** with ADR-0061 — the validator's
verdict becomes the brain's feedback signal. The brain reads
`LastOutcome.Failed("validation: missing or empty: title, price", ...)`
and decides whether to revise the schema or move on.

### 6. Builder method `WithSchemaValidator`

Both builders (`ScraperEngineBuilder` and `AgentEngineBuilder`) get
the same method:

```csharp
public ScraperEngineBuilder WithSchemaValidator(ISchemaValidator validator);
public AgentEngineBuilder    WithSchemaValidator(ISchemaValidator validator);
```

The scraper builder's validator is injected into the constructed
`ExtractionRouter` / `SelfHealingContentExtractor` when those are
present; the agent builder's validator is injected into the
`AgentEngine`.

### Bounded scope (v1)

- **One validator at a time.** A `CompositeSchemaValidator` adapter
  (composes N validators; all-must-pass) is a future opt-in; not in v1.
  Consumer composes manually if needed.
- **Synchronous.** `Validate` returns `ValidationResult`, not
  `ValueTask<ValidationResult>`. The default impl is fast (recursive
  walk over the schema); an LLM validator wraps async-over-sync at
  the call site (see fork 3). A v2 `IAsyncSchemaValidator` lives at
  the async-aware composition layer if it earns its keep.
- **Validator does not mutate the result.** `Validate` is read-only;
  it does not insert defaults / clean fields / canonicalise data.
  Mutation is the page-processor's job (ADR-0038).
- **Schema is the policy owner; validator interprets.** Don't move
  "what counts as required" into a third place — `Schema` and
  `SchemaElement` already model required-ness via their structure
  (every named child is required by the current default; a future
  `IsOptional` is a Schema-side knob, not a validator-side one).

## Considered options

### Fork 1 — Return shape: `ValidationResult` vs. `bool` vs. exception

| Option | What | Verdict |
|---|---|---|
| (a) `ValidationResult(IsValid, Reason)` record | Sum-typed return; `Reason` populated only when invalid. | **Recommended.** The repairer and the agent both need the reason; a `bool` discards it. The record is two fields; the static `Valid` / `Invalid(...)` helpers cover both cases ergonomically. |
| (b) `bool IsValid(...)` | Current shape, no reason. | Rejected. Two callers (repairer, agent) need the reason; the shape would be too narrow. |
| (c) Throw on invalid; otherwise return | Exception-driven control flow. | Rejected. Validation failure is a *normal* outcome (router escalates, self-heal repairs, agent reports), not exceptional. ADR-0029's swallow-and-log discipline applied one level out. |

### Fork 2 — Per-schema vs. global validator

| Option | What | Verdict |
|---|---|---|
| (a) Single global validator at the builder level | One `ISchemaValidator` registered per builder; same validator applies to every schema in every Extract / fallback / self-heal site. | **Recommended.** Consistency; the validator's policy is a build-time decision. Per-schema specialisation is a v2 enhancement when a real caller surfaces it. |
| (b) Per-schema validator | `Schema` carries its own validator reference. | Rejected. Bloats `Schema` for a feature no current caller has named; v2 deferral. |
| (c) Per-call validator override | `ExtractAsync(..., ISchemaValidator? overrideValidator = null)`. | Rejected. Widens `IContentExtractor.ExtractAsync` signature — every adapter signs the change. The seam stays narrow. |

### Fork 3 — Sync vs. async

| Option | What | Verdict |
|---|---|---|
| (a) Sync `Validate` returning `ValidationResult` | One method, sync, fast. | **Recommended.** The default impl is a recursive walk (microseconds); the async-over-sync wrap for an LLM validator lives at the wrapper site. Avoiding async at the seam keeps consumer code simple. |
| (b) Async `ValueTask<ValidationResult> ValidateAsync` | Sum-typed result via async; every consumer awaits. | Rejected. Async adds ceremony at three call sites (router, self-heal, agent) for a feature only the LLM-validator needs; the LLM-validator can wrap. |
| (c) Two seams — sync + async sister | `ISchemaValidator` + `IAsyncSchemaValidator`. | Rejected (v2 deferral). Speculative duplication; one seam plus an async-aware wrapper is the simpler shape. |

### Fork 4 — "Required" policy ownership: Schema or Validator

| Option | What | Verdict |
|---|---|---|
| (a) `Schema` marks fields required (existing — every child is required by default); validator interprets | The validator reads `Schema` and applies the per-element rule. | **Recommended.** Don't move the policy. Adding a future `SchemaElement.IsOptional` is a Schema-side widening; the validator's interpretation rule (string-empty triggers; int 0 doesn't) stays a per-validator decision. |
| (b) Move "required" onto the validator | Validator owns a per-field map "these are required, these aren't." | Rejected. Two sources of truth for the schema shape; structural drift inevitable. |

### Fork 5 — Multiple validators (composition)

| Option | What | Verdict |
|---|---|---|
| (a) Single in v1; consumer composes manually | One validator per builder; a consumer who wants two writes a `class MyCombinedValidator : ISchemaValidator`. | **Recommended.** Smallest surface; the composition shape is whatever the consumer needs. |
| (b) `CompositeSchemaValidator` adapter in core | One in core, all-must-pass / any-must-pass options. | Rejected (v2 deferral). Speculative; consumer-authored composition is one class. |

### Fork 6 — Agent driver invokes the validator on Extract decisions

| Option | What | Verdict |
|---|---|---|
| (a) Yes — engine validates; failure becomes `Failed("validation: ...")` outcome | The brain reads the structured failure next step. | **Recommended.** Closes the brain's feedback loop on Extract decisions; ADR-0061's outcome closure becomes load-bearing. |
| (b) No — engine emits whatever the extractor returns | Validator runs only in router / self-heal; agent trusts the extractor. | Rejected. The agent's brain-chosen Schema can be arbitrary (the brain picks fields each step — ADR-0051 fork 4); without validator feedback, the brain doesn't learn its mistake. |

### Fork 7 — Validator order in the pipeline relative to page processors

| Option | What | Verdict |
|---|---|---|
| (a) Validator runs *before* page processors | Pipeline order: extractor → validator → processors → sinks. | **Recommended.** The validator's job is "did the extractor succeed?" — page processors enrich / observe / drop the *valid* record. Running validator first means a failed extraction never reaches a processor that expects valid data. |
| (b) Validator is itself a page processor | Register `SchemaValidationProcessor` in the pipeline. | Rejected. Confuses two roles. Page processors are pluggable observers; the validator is an integral part of the extraction *strategy* (the proposer-validator pair). Wrong layer. |

## Consequences

- **The proposer-validator pattern's missing half is now a seam.**
  Three docks (router, self-heal, agent Extract) consume the same
  validator interface; swapping the policy is one `WithSchemaValidator`
  call.
- **The router and self-heal extractor lose direct dependencies on
  `SchemaSatisfiedValidator`.** Their constructors take `ISchemaValidator`;
  the static method is marked obsolete and removed in v11.
- **Failed validations surface as structured outcomes to the agent
  brain.** ADR-0061's `Failed("validation: <reason>")` is now a real
  composition — the brain reads the per-field failure list and can
  revise its schema.
- **LLM-validator earns a future ADR for free.** `LlmSchemaValidator` is
  one ~30-line class composing `LlmCall<ValidationResult>` (ADR-0059) +
  a per-call descriptor. The seam is in place; the LLM impl ships when
  a caller asks.
- **Breaking change for v10.x.** `ExtractionRouter`'s
  `Func<JsonObject, Schema?, bool>?` constructor parameter is replaced
  by `ISchemaValidator?`; `SelfHealingContentExtractor`'s constructor
  gains the parameter. Migration is one line per caller; consumers
  using `WithLlmFallback(...)` / `WithLlmSelfHealing(...)` /
  `WithSelfHealing(...)` see no change (the satellite shims preserve
  defaults).
- **`SchemaSatisfiedValidator.IsSatisfied` is obsolete.** Backward-
  compat shim for v10.x; removed in v11.
- **CONTEXT.md** gains a **Schema validator** term — sibling to the
  **Content extractor**. Cross-links to **Extraction router**, **Self-
  healing content extractor**, and **Agent brain** (now-validated via
  ADR-0061's `LastOutcome.Failed`).
- **CLAUDE.md** gets a gotcha — `ISchemaValidator` is now a seam;
  default `SchemaSatisfiedValidator` preserves ADR-0029 behaviour;
  swap via `WithSchemaValidator`. The `SelfHealingContentExtractor`
  constructor signature changes (now takes the optional validator).

## Bounded scope (v1)

- **`CompositeSchemaValidator`** — consumer composes manually in v1.
- **Per-schema validator** — global only in v1.
- **`IAsyncSchemaValidator`** — sync + async-wrap in v1.
- **`SchemaElement.IsOptional`** — Schema-side enhancement, separate
  ADR if pursued.
- **`LlmSchemaValidator`** — the LLM-backed validator the seam exists
  to enable. Lives in `WebReaper.AI`, ships when a real caller asks.
- **Validator-driven page-processor short-circuit** — processors run
  on valid extractions only in v1; a future "run processors on
  failed extractions too" knob is not designed.

## Implementation (slice, when accepted)

**Core seam + default:**

1. **`WebReaper/Core/Parser/Abstract/ISchemaValidator.cs`** — new
   public interface with `ValidationResult`.
2. **`WebReaper/Core/Parser/Concrete/SchemaSatisfiedValidator.cs`** —
   refactored from static class to `ISchemaValidator`-implementing
   sealed class; static `Instance` singleton; static `IsSatisfied`
   marked `[Obsolete]` and delegates.

**Composers:**

3. **`WebReaper/Core/Parser/Concrete/ExtractionRouter.cs`** —
   constructor signature change: `Func<JsonObject, Schema?, bool>?`
   → `ISchemaValidator?`. Default `SchemaSatisfiedValidator.Instance`.
4. **`WebReaper/Core/Parser/Concrete/SelfHealingContentExtractor.cs`** —
   constructor signature change: new optional `ISchemaValidator?`
   parameter. Both validation call sites swap.
5. **`WebReaper/Core/Parser/Abstract/ISelectorRepairer.cs`** —
   `RepairAsync` signature widens with `string? failureReason`.
6. **`WebReaper.AI/LlmSelectorRepairer.cs`** — reads `failureReason`
   and injects into the prompt; system prompt updated.

**Agent driver:**

7. **`WebReaper/Core/Agent/Concrete/AgentEngine.cs`** — constructor
   gains `ISchemaValidator validator` (default
   `SchemaSatisfiedValidator.Instance`); `Extract` switch arm calls
   `validator.Validate(...)`; failed → `Failed("validation: ...")`
   outcome (ADR-0061 composition).

**Builders:**

8. **`WebReaper/Builders/ScraperEngineBuilder.cs`** — `WithSchemaValidator`
   method; wires into router / self-heal constructors at build time.
9. **`WebReaper/Builders/AgentEngineBuilder.cs`** — `WithSchemaValidator`
   method; passes through to engine constructor.

**Tests:**

10. **`WebReaper.Tests/WebReaper.UnitTests/SchemaSatisfiedValidatorTests.cs`**
    — renamed from existing (if any); pin every ADR-0029 per-element
    rule; new tests pin `Reason` content on each failure mode
    (missing field name path; empty-list path; etc.).
11. **`WebReaper.Tests/WebReaper.UnitTests/ExtractionRouterTests.cs`** —
    existing tests, signature update; new test: custom validator
    returning `Invalid("custom")` routes to fallback with the reason
    in logs.
12. **`WebReaper.Tests/WebReaper.UnitTests/SelfHealingContentExtractorTests.cs`**
    — existing tests, signature update; new test: custom validator
    triggers repair, the `failureReason` propagates into the
    repairer's invocation.
13. **`WebReaper.Tests/WebReaper.UnitTests/AgentEngineDriverTests.cs`** —
    new test: stub validator returning `Invalid("test")` on Extract
    decision produces `LastOutcome = Failed("validation: test", null)`;
    loop continues; brain sees the failure next step.

**Docs:**

14. **CONTEXT.md** — new **Schema validator** term; cross-links to
    Extraction router, Self-healing content extractor, Agent brain;
    relationship line.
15. **CLAUDE.md** — gotcha on the seam, the default, the constructor
    change.

### Guardrails (when implemented)

- `dotnet build WebReaper.sln` — 0 errors. (The router constructor
  signature change is a breaking edge; consumers using
  `WithFallbackExtractor(...)` / `WithLlmFallback(...)` see no change.)
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — all pass; new
  validator / router / self-heal / agent tests pass.
- `dotnet test WebReaper.Tests/WebReaper.AI.Tests` — all pass;
  `LlmSelectorRepairer` reads the new `failureReason` argument.
- `WebReaper.AotSmokeTest` — unchanged (validator + record are
  AOT-safe; no reflection).

## References

- ADR-0001 — closed-sum pattern; `ValidationResult` is two-arm (Valid
  / Invalid) shape.
- ADR-0029 — coercion-failure policy; the "what counts as missing"
  rule the default validator preserves.
- ADR-0036 — "shape from the second adapter, not for it" — this ADR
  is the second-adapter arrival for the validator.
- ADR-0038 — page processor seam; sibling concept (post-extract);
  validator runs *before* processors.
- ADR-0039 — `IContentExtractor` seam; the sibling seam this one
  complements.
- ADR-0046 — extraction router; the first caller, signature update.
- ADR-0047 — self-healing content extractor; the second caller,
  signature update; `ISelectorRepairer` widens with `failureReason`.
- ADR-0051 — agent driver; the third caller, gains the validator.
- ADR-0059 — `LlmCall<TResponse>`; the mechanism a future
  `LlmSchemaValidator` would compose on.
- ADR-0061 — `LastDecisionOutcome`; the structural composition —
  validator failure on `Extract` becomes `Failed("validation: ...")`
  in `LastOutcome`.
- ADR-0064 — `.UseAi(...)`; the policy a `LlmSchemaValidator` would
  wire through, alongside the other Llm registrations.
