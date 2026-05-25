# `AiPolicyMode.Inferred` — `.UseAi(...)` auto-wires the schema inferrer

## Status

**Accepted — design pass** (2026-05-25). First of two ADRs in the v10.0.0
pre-tag AI-native completion wave. Closes ADR-0067 Fork 7 (the
`.UseAi(...)` auto-wiring deferral). Pairs with ADR-0069 (validator-driven
re-inference). Folds into v10.0.0 — the tag waits on this PR.

## Context

ADR-0064 added `.UseAi(chatClient, opts?)` — the one-line aggregator that
wires the firecrawl-shaped triple (LLM-fallback extractor + self-healing
repairer + action resolver) in `AiPolicyMode.Recommended`. ADR-0067 added
`.ExtractInferred(goal?)` — the third `ICrawlSeed` strategy terminal —
but **left the `.UseAi(...)` integration explicitly out of v1** (ADR-0067
Fork 7):

> v1 keeps the inferrer's wiring **explicit** —
> `.ExtractInferred(goal).WithLlmSchemaInferrer(client)` (or via
> `.UseAi(client).WithLlmSchemaInferrer(client)`). Auto-wiring would
> entangle the inferrer with the existing extractor wiring
> (`.UseAi(Recommended)` wires `WithLlmFallback`, which conflicts
> semantically with `LearnedSchemaContentExtractor`). v2 may add an
> `AiPolicyMode.Inferred` arm or a `WireInferrer: true` flag on
> `AiOptions`.

That conflict is real — `LlmFallback` wraps the deterministic
`SchemaFold` with an LLM fallback at the same seam slot the
`LearnedSchemaContentExtractor` wrapper occupies, and the two can't
both be the registered `IContentExtractor`. The v1 deferral was the
right call.

The friction is now real too: the consumer-facing one-liner for the
five firecrawl-parity strategies is asymmetric —

| Strategy | Seed terminal | Policy one-liner |
|---|---|---|
| Schema-driven | `.Extract(schema)` | `.UseAi(client, new AiOptions(Policy: Recommended))` |
| LLM-primary  | `.Extract(schema)` | `.UseAi(client, new AiOptions(Policy: LlmPrimary))` |
| Extraction-only | `.Extract(schema)` | `.UseAi(client, new AiOptions(Policy: ExtractionOnly))` |
| Markdown | `.AsMarkdown()` | (no `.UseAi(...)` needed) |
| **Inferred** | `.ExtractInferred(goal?)` | **`.UseAi(client).WithLlmSchemaInferrer(client)`** ← two calls |

Every other AI-bearing strategy has a one-line policy. Inferred needs
two. v10.0.0 ships the asymmetry; this ADR removes it.

### What this ADR does and does not move

**Moves into satellite (`WebReaper.AI`):**
- New `AiPolicyMode.Inferred` arm on the enum.
- `AiOptions.Inferrer` per-role override (`LlmSchemaInferrerOptions?`).
- `AiOptions.ResolveInferrerOptions()` synthesis helper.
- `UseAiRegistration.UseAi(ScraperEngineBuilder, ...)` switch gains the
  `Inferred` case.
- `UseAiRegistration.UseAi(AgentEngineBuilder, ...)` switch gains an
  actionable throw on `Inferred` — the agent's brain proposes its own
  schemas in `AgentDecision.Extract(schema)`, so a separate inferrer
  arm is structurally redundant on that builder.

**Stays out of scope (v1 of the policy):**
- **No self-heal composition with the inferrer.** `Inferred` wires
  the inferrer + action resolver only; not self-healing repairer.
  The repairer mutates the schema's selectors, but the inferred schema
  hasn't yet had its selectors validated against the producing page's
  structure — composing repair on the inferred schema is a v2 question
  (Fork 5).
- **No "smart Recommended" auto-detection of the seed terminal.**
  Modes describe a canned wiring; reading the seed marker at
  `UseAi` time and adapting the wiring would break the closed-sum
  discipline (the mode name no longer matches its behaviour). Fork 1.
- **No "WireInferrer: true" flag on `AiOptions`.** The `AiPolicyMode`
  enum is the project's canonical pick-one-policy seam; adding a
  parallel boolean flag splits the surface. The flag was named in
  ADR-0067 Fork 7 as an alternative; rejected here.
- **No agent-side `Inferred` arm.** The brain already proposes schemas
  per Extract decision; an inferrer arm is structurally redundant.
  Throw at `.UseAi(...)` with an actionable message.

## Decision

One new arm on the enum, one new per-role override on `AiOptions`, one
new switch case on each builder's `UseAi` extension.

### 1. `AiPolicyMode.Inferred` (satellite enum)

`WebReaper.AI/AiOptions.cs`. Add the 5th arm:

```csharp
/// <summary>
/// Runtime schema inference — the "extract structured data without a
/// schema" path (ADR-0067 + ADR-0068).
/// <para>
/// <b>Scraper:</b> wires <see cref="LlmSchemaInferrerRegistration.WithLlmSchemaInferrer"/>
/// (so the wrapper composed at <c>BuildAsync</c> for
/// <c>.ExtractInferred(...)</c> has a real inferrer) +
/// <see cref="LlmActionResolverRegistration.WithLlmActionResolver"/>
/// (the orthogonal action surface — useful regardless of extraction
/// strategy). Mutually exclusive with
/// <see cref="LlmExtractorRegistration.WithLlmFallback"/> and
/// <see cref="LlmExtractorRegistration.WithLlmExtractor"/> — those
/// register an <see cref="WebReaper.Core.Parser.Abstract.IContentExtractor"/>
/// that would shadow the <see cref="WebReaper.Core.Parser.Concrete.LearnedSchemaContentExtractor"/>
/// wrapper. The consumer-facing one-liner:
/// <code>
/// var engine = await ScraperEngineBuilder
///     .Crawl("https://shop.com/products")
///     .ExtractInferred(goal: "product details")
///     .UseAi(chatClient, new AiOptions(Policy: AiPolicyMode.Inferred))
///     .WriteToConsole()
///     .BuildAsync();
/// </code>
/// </para>
/// <para>
/// <b>Agent:</b> not supported. The agent's brain proposes its own
/// schemas in <c>AgentDecision.Extract(schema)</c>; a separate
/// inferrer arm is structurally redundant on that builder.
/// <c>.UseAi(agentBuilder, AiOptions(Policy: Inferred))</c> throws
/// <see cref="ArgumentOutOfRangeException"/> with an actionable
/// message pointing at <see cref="Recommended"/> / <see cref="LlmPrimary"/>.
/// </para>
/// </summary>
Inferred,
```

### 2. `AiOptions.Inferrer` per-role override

Add the field to the positional record:

```csharp
public sealed record AiOptions(
    string? Model = null,
    float Temperature = 0.0f,
    int MaxResponseTokens = 4096,
    bool MarkdownPreClean = true,
    LlmExtractorOptions? Extractor = null,
    LlmActionResolverOptions? Resolver = null,
    LlmAgentBrainOptions? Brain = null,
    LlmExtractorOptions? Repairer = null,
    LlmSchemaInferrerOptions? Inferrer = null,        // NEW
    AiPolicyMode Policy = AiPolicyMode.Recommended,
    CachePolicy CachePolicy = CachePolicy.Hinted)
```

Same per-role override discipline as the four existing per-role
records — `null = inherit from global defaults via Resolve* helper`;
non-null wins for every field.

### 3. `ResolveInferrerOptions()` synthesis helper

Mirror of `ResolveExtractorOptions` — flow global defaults down when
the per-role record is null; respect the per-role record when
non-null (with `CachePolicy ??= global` to preserve the per-role
nullable-inherit convention):

```csharp
internal LlmSchemaInferrerOptions ResolveInferrerOptions()
    => Inferrer is null
        ? new LlmSchemaInferrerOptions(
            Model: Model,
            UseMarkdownPreClean: MarkdownPreClean,
            MaxContentChars: 32_000,        // inferrer's own default
            MaxResponseTokens: 1024,         // inferrer's own default (small JSON)
            Temperature: Temperature,
            SystemPrompt: null,
            CachePolicy: CachePolicy)
        : Inferrer with { CachePolicy = Inferrer.CachePolicy ?? CachePolicy };
```

Note: the global `MaxResponseTokens` does NOT flow into the inferrer's
`MaxResponseTokens` when synthesised from defaults — the inferrer's
1024-token cap is role-specific (the response is a small JSON object
naming each field's CSS selector; the global 4096 default is the
extractor's response cap, which is a different shape). Matches the
resolver's `Math.Min(MaxResponseTokens, 512)` pattern from ADR-0064.
The per-role record overrides when non-null.

### 4. Scraper-side `UseAi` switch

`WebReaper.AI/UseAiRegistration.cs`. Add the `Inferred` case:

```csharp
return options.Policy switch
{
    AiPolicyMode.Recommended    => /* unchanged */,
    AiPolicyMode.LlmPrimary     => /* unchanged */,
    AiPolicyMode.ExtractionOnly => /* unchanged */,
    AiPolicyMode.Inferred       => builder
        .WithLlmSchemaInferrer(chatClient, inferrerOpts)
        .WithLlmActionResolver(chatClient, resolverOpts),
    AiPolicyMode.None           => builder,
    _ => throw new ArgumentOutOfRangeException(/* ... */),
};
```

`var inferrerOpts = options.ResolveInferrerOptions();` synthesised
upfront alongside the existing resolvers.

### 5. Agent-side `UseAi` switch

`WebReaper.AI/UseAiRegistration.cs`. Add an actionable throw — the
agent's brain proposes schemas per decision, so a separate inferrer
arm is structurally redundant:

```csharp
switch (options.Policy)
{
    case AiPolicyMode.Recommended:    /* unchanged */ break;
    case AiPolicyMode.LlmPrimary:     /* unchanged */ break;
    case AiPolicyMode.ExtractionOnly: /* unchanged */ break;
    case AiPolicyMode.Inferred:
        throw new ArgumentOutOfRangeException(
            nameof(options),
            options.Policy,
            "AiPolicyMode.Inferred is not supported on AgentEngineBuilder. " +
            "The agent's brain proposes schemas per AgentDecision.Extract(schema); " +
            "an external inferrer arm is structurally redundant. Use " +
            "AiPolicyMode.Recommended (deterministic + LLM fallback + " +
            "self-heal + action resolver) or AiPolicyMode.LlmPrimary " +
            "(LLM extractor + self-heal + action resolver) instead.");
    case AiPolicyMode.None: /* unchanged */ break;
    default: throw new ArgumentOutOfRangeException(/* ... */);
}
```

### 6. Build-time validation (no change)

`ScraperEngineBuilder.BuildAsync` already throws when
`.ExtractInferred(...)` was called and no `ISchemaInferrer` is
registered (ADR-0067). `.UseAi(client, Inferred)` registers one, so
the check passes. A consumer who calls `.UseAi(Inferred)` without
`.ExtractInferred(...)` first registers an inferrer that's silently
unused — same shape as today's
`.UseAi(client).WithLlmSchemaInferrer(client)` without a seed terminal.

The reverse — `.ExtractInferred(...).UseAi(Recommended)` — throws at
build time with the actionable ADR-0067 message ("call
`.WithLlmSchemaInferrer(chatClient)`, or supply a custom
`ISchemaInferrer`"). No new check; the existing one covers it.

### 7. Consumer-facing surface

```csharp
// One-liner (the headline):
var engine = await ScraperEngineBuilder
    .Crawl("https://shop.com/products")
    .ExtractInferred(goal: "product details")
    .UseAi(chatClient, new AiOptions(Policy: AiPolicyMode.Inferred))
    .WriteToConsole()
    .BuildAsync();

// Per-role override:
var opts = new AiOptions(
    Policy: AiPolicyMode.Inferred,
    Inferrer: new LlmSchemaInferrerOptions(
        Model: "gpt-4o-mini",
        MaxContentChars: 16_000));
var engine = await ScraperEngineBuilder
    .Crawl("https://shop.com/products")
    .ExtractInferred(goal: "product details")
    .UseAi(chatClient, opts)
    .WriteToConsole()
    .BuildAsync();

// À la carte still works (the v10.0.0 path):
var engine = await ScraperEngineBuilder
    .Crawl("https://shop.com/products")
    .ExtractInferred(goal: "product details")
    .WithLlmSchemaInferrer(chatClient)
    .WriteToConsole()
    .BuildAsync();
```

## Considered options

### Fork 1 — Smart-Recommended vs Explicit Inferred mode

| Option | What | Verdict |
|---|---|---|
| (a) Explicit `Inferred` arm | New 5th enum value; consumers pick the right mode for the right seed terminal. | **Recommended.** Closed-sum discipline preserved; the mode name maps 1:1 to its wiring. Documented surface area; the enum is the canonical pick-one-policy seam. |
| (b) Smart `Recommended` | `Recommended` reads the seed marker; auto-wires the inferrer when `.ExtractInferred()` was used, else the existing fall-back triple. | Rejected. Mode name no longer matches wiring (you read "Recommended" but get inferrer wiring). Breaks the project's "modes are canned compositions" discipline. More magic; more surprises. |
| (c) Both `Inferred` arm AND smart `Recommended` | Belt-and-braces. | Rejected. Two paths to the same wiring; documentation becomes the discriminator; more test surface. |
| (d) `WireInferrer: true` flag on `AiOptions` | Boolean flag alongside `Policy`. | Rejected. Splits the surface — picking a mode is the project's existing seam; adding a parallel boolean is the wrong shape. Named as an alternative in ADR-0067 Fork 7. |

### Fork 2 — What does `Inferred` wire besides the inferrer?

| Option | What | Verdict |
|---|---|---|
| (a) Inferrer + action resolver | Two adapters; the action resolver is orthogonal to extraction strategy. | **Recommended.** The action resolver is useful regardless of extraction path; bundling it preserves the firecrawl-shaped triple feel. Self-heal is rejected separately (Fork 3). |
| (b) Inferrer only | One adapter; consumer adds action resolver via `.WithLlmActionResolver(...)` if needed. | Rejected. Asymmetric with the other AI-bearing modes — every other mode wires the action resolver. |
| (c) Inferrer + action resolver + self-heal | Three adapters. | Rejected (Fork 3 — self-heal not composable cleanly with the inferred schema in v1). |

### Fork 3 — Self-heal composition with the inferrer

| Option | What | Verdict |
|---|---|---|
| (a) Don't compose self-heal with the inferrer | `Inferred` wires the inferrer + action resolver; not the repairer. | **Recommended (v1).** The repairer mutates a Schema's selectors based on validation failures; the inferred schema hasn't yet had its selectors validated against the producing page's structure. Composing repair on the inferred schema makes the failure mode harder to reason about (which selector — the inferred one or the repaired one — should the validator-driven re-inference of ADR-0069 trigger on?). v2 question. |
| (b) Compose self-heal + inferrer | Both wrap the inner extractor; the inferrer runs first, then self-heal runs on the produced schema's failures. | Rejected (v1). Layering correctness; failure-mode entanglement with ADR-0069's re-inference trigger. |

### Fork 4 — Per-role options shape

| Option | What | Verdict |
|---|---|---|
| (a) Add `LlmSchemaInferrerOptions? Inferrer` field to `AiOptions` | Mirror of `Extractor`/`Resolver`/`Brain`/`Repairer`. | **Recommended.** Symmetric with the existing per-role fields; `Resolve*` helper extends the pattern; consumers reading the AiOptions ctor see the parallel structure. |
| (b) Embed inferrer knobs as scalar fields on `AiOptions` | `InferrerMaxContentChars`, `InferrerSystemPrompt`, … | Rejected. Splits the surface; inconsistent with the per-role record pattern; bigger options ctor. |

### Fork 5 — Agent builder's behaviour on `Inferred`

| Option | What | Verdict |
|---|---|---|
| (a) Throw with actionable message | The brain already proposes schemas; a separate inferrer is structurally redundant. | **Recommended.** Loud failure with a pointer at the right modes. The agent's `AgentDecision.Extract(schema)` IS the inferrer — wiring a separate `ISchemaInferrer` would create two competing sources of truth. |
| (b) No-op (silently ignore) | The brain still works; the inferrer registration just goes unused. | Rejected. Silent surprises — consumer reads the mode docs, expects inference behaviour, gets none. Loud is better. |
| (c) Wire the inferrer alongside the brain | Both register; brain proposes per-step, inferrer infers once per crawl. | Rejected. Two competing sources of truth; semantics undefined (whose schema wins on a given Extract decision?); a v2 question if a real use case appears. |

### Fork 6 — CachePolicy default for the `Inferred`-wired inferrer

| Option | What | Verdict |
|---|---|---|
| (a) Inherit `AiOptions.CachePolicy` (`Hinted` by default) | The synthesised inferrer options carry `Hinted` when `.UseAi(...)` wires them. | **Recommended.** Matches the existing per-role inheritance — Anthropic users on `.UseAi(...)` get cache benefits across all wired adapters; OpenAI / Gemini users see no change. Consumers wiring via `.WithLlmSchemaInferrer(client)` à la carte still get the safer `Default` (ADR-0067). The split honours the "explicit wiring → defensive default; .UseAi(...) → cheap default" pattern. |
| (b) Always default to `CachePolicy.Default` | Override the `AiOptions.CachePolicy` flow for the inferrer specifically. | Rejected. Inconsistent with the other per-role flow; consumer would expect `Hinted` to propagate. The single-page-amortisation concern from ADR-0067 only matters for adapters that fire once total — and that's true of the inferrer regardless of policy mode. The `.UseAi(...)` consumer asked for caching; honour it. |

## Consequences

- **The five firecrawl-parity strategies all have a one-line policy.**
  The asymmetric "two calls for inferred" friction (named in the
  context section) is gone:
  ```csharp
  .Extract(schema).UseAi(client)                                                                       // Recommended
  .Extract(schema).UseAi(client, new AiOptions(Policy: LlmPrimary))                                    // LlmPrimary
  .Extract(schema).UseAi(client, new AiOptions(Policy: ExtractionOnly))                                // ExtractionOnly
  .AsMarkdown()                                                                                        // Markdown (no UseAi)
  .ExtractInferred(goal: "...").UseAi(client, new AiOptions(Policy: Inferred))                         // Inferred (NEW)
  ```
- **`AiPolicyMode` grows 4 → 5 arms.** Per the closed-sum discipline,
  the enum addition is a non-breaking minor change; the
  `ArgumentOutOfRangeException` default in the existing switches
  already covers "unknown" arms, so existing consumers see no
  behaviour change.
- **`AiOptions` grows by one positional field (`Inferrer`).** Pure
  additive; consumers using `new AiOptions(...)` with named args see
  no break; consumers using positional construction past `Repairer`
  (rare) get a compile error and add the new field.
- **The agent builder's `.UseAi(Inferred)` throws at registration
  time.** Surfaces the misuse loudly. The error message points at the
  two right modes.
- **CONTEXT.md** gains one term — **AI policy mode** entry updated to
  list the 5th arm — and the Relationships section's `.UseAi(...)`
  line updated.
- **CLAUDE.md** gets one new gotcha bullet on the `Inferred` arm
  (scraper-only; the cache-policy `Hinted` inheritance; the
  mutual-exclusion with `LlmFallback` / `LlmExtractor`).

## Bounded scope (v1 of the policy)

- **No self-heal composition.** Fork 3 — v2 question.
- **No "smart Recommended" auto-detection.** Fork 1 — closed-sum
  discipline.
- **No `WireInferrer: true` flag.** Fork 1 — splits the surface.
- **No agent-side `Inferred`.** Fork 5 — throw, don't no-op.
- **No bundle of the v10.0.0 `Markdown` strategy into the policy
  enum.** `.AsMarkdown()` remains the seed-terminal-only path —
  deterministic, no LLM, no policy mode needed.

## Implementation (slice, when accepted)

**Satellite — two edits, no new files:**

1. **`WebReaper.AI/AiOptions.cs`** —
   - Add `Inferred` arm to `AiPolicyMode` enum.
   - Add `LlmSchemaInferrerOptions? Inferrer = null` field to
     `AiOptions` positional record.
   - Add `internal LlmSchemaInferrerOptions ResolveInferrerOptions()`
     helper following the `ResolveExtractorOptions` pattern.
2. **`WebReaper.AI/UseAiRegistration.cs`** —
   - Scraper switch: synthesise `inferrerOpts` upfront; add the
     `AiPolicyMode.Inferred` case wiring `WithLlmSchemaInferrer +
     WithLlmActionResolver`.
   - Agent switch: add the `Inferred` case as an actionable throw
     (matching the `default:` shape).

**Tests — `WebReaper.AI.Tests/UseAiInferredTests.cs`:**

3. New file pinning the new wiring:
   - `.UseAi(client, Policy: Inferred)` after `.ExtractInferred()`
     builds successfully + the wrapper composes (verified via the
     existing `SchemaInferrerForTests` accessor on the builder).
   - The wired inferrer is `LlmSchemaInferrer` (not `NullSchemaInferrer`).
   - Action resolver is also wired (the orthogonal arm).
   - LlmFallback / LlmExtractor are NOT wired (the mutually-exclusive
     arms).
   - Per-role `LlmSchemaInferrerOptions` override propagates to the
     wired inferrer (via reflection on options or via a no-op stub
     `IChatClient` that captures the descriptor).
   - `.UseAi(...)` synthesised inferrer inherits `CachePolicy.Hinted`
     when `AiOptions.CachePolicy` is the default `Hinted`.
   - `.UseAi(...)` synthesised inferrer respects per-role `CachePolicy`
     override.
   - Agent builder's `.UseAi(Inferred)` throws
     `ArgumentOutOfRangeException` with the actionable message.
   - Scraper builder's `.UseAi(Inferred)` without `.ExtractInferred()`
     builds successfully (inferrer registered but unused) — silently
     ignored per ADR-0067 semantics.
   - Scraper builder's `.ExtractInferred().UseAi(Recommended)` (the
     wrong-mode pairing) throws the existing ADR-0067 build-time
     "no ISchemaInferrer registered" message — no change.

**Docs:**

4. **CONTEXT.md** — AI policy mode entry updated to mention the 5th
   arm + the `Inferred` wiring.
5. **CLAUDE.md** — section header `ADR-0040..0067` →
   `ADR-0040..0069` (paired with ADR-0069 doc commit); one new
   gotcha bullet on `Inferred`.
6. **CHANGELOG.md** — new "10.0.0 — AI-native completion wave
   (ADR-0068 + ADR-0069)" subsection.

### Guardrails (when implemented)

- `dotnet build WebReaper.sln` — 0 errors.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — all existing
  tests pass.
- `dotnet test WebReaper.Tests/WebReaper.AI.Tests` — all existing
  tests pass; new `UseAiInferredTests` pass.
- `dotnet publish WebReaper.AotSmokeTest -c Release` — native code
  generated; no IL-trim warnings (the change is satellite-only;
  AOT-clean by ADR-0009 quarantine).

## References

- ADR-0009 — registration seam + satellite pattern.
- ADR-0046 — `WithFallbackExtractor` / `WithLlmFallback` — the
  mutually-exclusive arm with the inferrer at the
  `IContentExtractor` seam.
- ADR-0050 — `WithLlmActionResolver` — the orthogonal action surface
  bundled into `Inferred`.
- ADR-0064 — `.UseAi(...)` aggregator; this ADR extends it.
- ADR-0065 — system-prompt caching; the per-role `CachePolicy`
  inheritance the inferrer joins.
- ADR-0066 — engine cost telemetry; the per-builder
  `LlmCallTelemetry` handle the wired inferrer threads through.
- ADR-0067 — `ISchemaInferrer` + `LearnedSchemaContentExtractor` +
  `.ExtractInferred(...)`; Fork 7 (the v1 deferral this ADR closes).
- ADR-0069 — validator-driven re-inference; the sibling ADR in this
  wave.
