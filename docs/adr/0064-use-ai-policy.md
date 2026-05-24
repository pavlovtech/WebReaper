# `.UseAi(IChatClient, AiOptions?)` — one-line AI enablement; `WithLlm*` stays à la carte

## Status

**Proposed** (2026-05-24). Sixth and last ADR of the post-AI-native-
wave deepening campaign. Consolidates the five `WithLlm*` builder
extensions on two builders into one headline entry point per
builder. À la carte `WithLlm*` methods stay for fine-tuning. The
deep entry point trades five-method ceremony for one method + an
options bag — the firecrawl-shaped "one line to AI-enable a crawler"
that ADR-0044..0051 designed *for* without ever offering. Folds into
the same v10.x release.

## Context

After the AI-native wave (ADR-0040..0051) and ADR-0059..0063, the
satellite ships these registration extensions on the two builders:

**`ScraperEngineBuilder` (scraper-side):**
- `WithLlmExtractor(chatClient, opts?)` — replace the deterministic extractor with the LLM.
- `WithLlmFallback(chatClient, opts?)` — route deterministic-first → LLM-fallback.
- `WithLlmSelfHealing(chatClient, opts?)` — wrap deterministic with LLM repair.
- `WithLlmActionResolver(chatClient, opts?)` — register the LLM action resolver.
- (Future: `WithLlmSchemaValidator(chatClient, opts?)` once ADR-0062's seam has an LLM impl.)

**`AgentEngineBuilder` (agent-side):**
- `WithLlmBrain(chatClient, opts?)` — register the LLM brain.
- `WithLlmExtractor(chatClient, opts?)` — replace the agent's content extractor.
- `WithLlmActionResolver(chatClient, opts?)` — register the LLM action resolver for `Act` decisions.
- `WithLlmSelfHealing(chatClient, opts?)` — wrap the agent's extractor with self-healing.

Every method takes the **same `IChatClient`** instance — five times on
the scraper, four times on the agent. The current consumer-facing
shape:

```csharp
// scraper:
var engine = await ScraperEngineBuilder
    .Crawl("https://shop.com").Extract(schema)
    .WithLlmFallback(client)
    .WithLlmSelfHealing(client)
    .WithLlmActionResolver(client)
    .WriteToConsole()
    .BuildAsync();

// agent:
var engine = await AgentEngineBuilder
    .Start("https://shop.com", "get all products")
    .WithLlmBrain(client)
    .WithLlmActionResolver(client)
    .WithLlmExtractor(client)
    .BuildAsync();
```

Three frictions:

1. **Repetition.** The `client` is the same in every call; the
   consumer threads it through five (or four) methods.
2. **No coherent "AI on" defaults.** A consumer who wants the
   firecrawl-shaped "use LLM as fallback + self-heal + action
   resolver" combo writes three lines. There's no canonical
   "recommended AI configuration" the satellite ships.
3. **The agent vs. scraper asymmetry is hidden.** The agent's
   `WithLlmBrain` is *required* (per ADR-0051: no brain → run is
   useless); the scraper's `WithLlmExtractor` is *optional*
   (deterministic-first is the recommendation). A consumer reading
   the builder discovery surface has no signal which calls are
   load-bearing.

The firecrawl one-liner for the agent shipped in `LlmAgent.RunAsync`
(ADR-0051):

```csharp
await LlmAgent.RunAsync(url, goal, chatClient);
```

The equivalent for the scraper is the consumer wiring three
extensions by hand. The asymmetry is awkward — and the scraper's
"one canonical AI setup" question has a real answer (the firecrawl-
shaped recommended combo) that the satellite never names.

### What `.UseAi(...)` actually does

For the **scraper** builder, `.UseAi(client, opts?)` is the
firecrawl-shaped policy:

- Wires `WithLlmFallback(client, opts?.Extractor)` — deterministic
  primary, LLM fallback. (Mode `Recommended`.)
- Wires `WithLlmSelfHealing(client, opts?.Repairer)` — wraps the
  deterministic primary with LLM repair. (Mode `Recommended`.)
- Wires `WithLlmActionResolver(client, opts?.Resolver)` — so
  `SemanticAct` works. (Mode `Recommended`.)
- (When ADR-0062's `LlmSchemaValidator` ships in a later release:
  wires `WithLlmSchemaValidator(client, opts?.Validator)` —
  semantic validation. (Mode `Recommended`.))

For the **agent** builder, `.UseAi(client, opts?)` is the
must-have-plus-recommended policy:

- Wires `WithLlmBrain(client, opts?.Brain)` — always (the agent is
  useless without it).
- Wires `WithLlmExtractor(client, opts?.Extractor)` — the brain's
  `Extract` decisions use the LLM. (Mode `Recommended` /
  `LlmPrimary`.)
- Wires `WithLlmActionResolver(client, opts?.Resolver)` — so
  `Act(SemanticAct)` works. (Mode `Recommended` / `LlmPrimary`.)

The policy is mode-gated; `AiPolicyMode` is the closed set of canned
configurations.

### Per-role overrides

`AiOptions` is a record-with-nested-records — global defaults
flow down; per-role options override per field. The pattern matches
ADR-0044's per-role options-record-with-nullable-fields shape:

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
    AiPolicyMode Policy = AiPolicyMode.Recommended);
```

If the consumer sets `AiOptions.Temperature = 0.5f` but doesn't
provide a per-role `Extractor.Temperature`, the extractor inherits
`0.5f`. If they provide both, the per-role wins. The merging is
explicit — `EffectiveExtractorOptions(global, perRole)` is a static
function the registration uses; no surprises.

### `AiPolicyMode` — the canned configurations

```csharp
public enum AiPolicyMode
{
    /// <summary>(Scraper) LLM-fallback extractor + selfheal repairer + action resolver.
    /// (Agent) Brain + LLM-fallback extractor + action resolver. The cheap path; LLM as
    /// rescue, not as default. The firecrawl-shaped recommendation.</summary>
    Recommended,

    /// <summary>(Scraper) LLM-primary extractor + selfheal repairer + action resolver.
    /// (Agent) Brain + LLM-primary extractor + action resolver. The "trust the model"
    /// path; LLM does extraction every time. Costs more, more flexible.</summary>
    LlmPrimary,

    /// <summary>(Scraper) extractor + fallback only; no action resolver, no brain
    /// wiring on the scraper path. (Agent) brain + extractor; no action resolver.
    /// For callers wanting AI on extraction but not on actions.</summary>
    ExtractionOnly,

    /// <summary>Explicit — wires nothing. <c>.UseAi(client, opts with { Policy = None })</c>
    /// is the test-harness escape hatch: AI is "available" (the chat client is registered)
    /// but the caller chooses à la carte. Defeats the sugar; useful for tests and bespoke
    /// configurations.</summary>
    None
}
```

The modes are mutually exclusive (enum, not flags). The composability
the bit-flag model would offer is exactly what à la carte
`WithLlm*` already exists for.

## Decision

Three pieces — one new options record + enum, two new extension methods
(one per builder). Both extension methods live in the existing
`WebReaper.AI` satellite. Existing `WithLlm*` methods stay; the new
sugar wraps them.

### 1. `AiOptions` + `AiPolicyMode`

`WebReaper.AI/AiOptions.cs`. Public record + enum:

```csharp
namespace WebReaper.AI;

public sealed record AiOptions(
    string? Model = null,
    float Temperature = 0.0f,
    int MaxResponseTokens = 4096,
    bool MarkdownPreClean = true,
    LlmExtractorOptions? Extractor = null,
    LlmActionResolverOptions? Resolver = null,
    LlmAgentBrainOptions? Brain = null,
    LlmExtractorOptions? Repairer = null,
    AiPolicyMode Policy = AiPolicyMode.Recommended);

public enum AiPolicyMode { Recommended, LlmPrimary, ExtractionOnly, None }
```

The same `LlmExtractorOptions` shape is reused for `Repairer` — the
existing repairer takes the extractor options record (`LlmSelectorRepairer`'s
ctor). No new option record introduced unnecessarily.

### 2. Scraper-side: `LlmRegistration.UseAi`

`WebReaper.AI/LlmRegistration.cs` (new file; existing per-role
`*Registration.cs` files stay):

```csharp
public static class LlmRegistration
{
    /// <summary>
    /// One-line AI enablement for the scraper builder (ADR-0064). Wires
    /// the LLM-fallback extractor, the self-healing repairer, and the
    /// action resolver per the supplied <see cref="AiPolicyMode"/>.
    /// </summary>
    public static ScraperEngineBuilder UseAi(
        this ScraperEngineBuilder builder,
        IChatClient chatClient,
        AiOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(chatClient);

        options ??= new AiOptions();
        var extractor = EffectiveExtractorOptions(options, options.Extractor);
        var resolver  = EffectiveResolverOptions(options, options.Resolver);
        var repairer  = EffectiveExtractorOptions(options, options.Repairer);

        return options.Policy switch
        {
            AiPolicyMode.Recommended    => builder
                                              .WithLlmFallback(chatClient, extractor)
                                              .WithLlmSelfHealing(chatClient, repairer)
                                              .WithLlmActionResolver(chatClient, resolver),
            AiPolicyMode.LlmPrimary     => builder
                                              .WithLlmExtractor(chatClient, extractor)
                                              .WithLlmSelfHealing(chatClient, repairer)
                                              .WithLlmActionResolver(chatClient, resolver),
            AiPolicyMode.ExtractionOnly => builder
                                              .WithLlmFallback(chatClient, extractor),
            AiPolicyMode.None           => builder, // explicit escape; à la carte
            _ => throw new ArgumentOutOfRangeException(nameof(options))
        };
    }

    private static LlmExtractorOptions EffectiveExtractorOptions(AiOptions global, LlmExtractorOptions? perRole)
        => new(
            Model:              perRole?.Model              ?? global.Model,
            UseMarkdownPreClean:perRole?.UseMarkdownPreClean ?? global.MarkdownPreClean,
            MaxTokens:          perRole?.MaxTokens           > 0 ? perRole.MaxTokens : global.MaxResponseTokens,
            Temperature:        perRole?.Temperature         ?? global.Temperature,
            SystemPrompt:       perRole?.SystemPrompt        ?? null);

    private static LlmActionResolverOptions EffectiveResolverOptions(AiOptions global, LlmActionResolverOptions? perRole)
        => new(
            Model:              perRole?.Model               ?? global.Model,
            Temperature:        perRole?.Temperature          ?? global.Temperature,
            MaxResponseTokens:  perRole?.MaxResponseTokens    > 0 ? perRole.MaxResponseTokens : Math.Min(global.MaxResponseTokens, 512),
            MaxHtmlChars:       perRole?.MaxHtmlChars         > 0 ? perRole.MaxHtmlChars       : 32_000,
            SystemPrompt:       perRole?.SystemPrompt         ?? null);
}
```

The merging helpers (`EffectiveExtractorOptions`, etc.) keep the per-
role per-field-nullable semantics explicit; no positional-record
games.

### 3. Agent-side: `LlmAgentRegistration.UseAi`

Sibling extension on `AgentEngineBuilder` (`WebReaper.AI/LlmAgentRegistration.cs`,
new file):

```csharp
public static class LlmAgentRegistration
{
    /// <summary>
    /// One-line AI enablement for the agent builder (ADR-0064). The
    /// brain is wired unconditionally (the agent is structurally useless
    /// without it — ADR-0051); the extractor and action resolver are
    /// wired per the supplied <see cref="AiPolicyMode"/>.
    /// </summary>
    public static AgentEngineBuilder UseAi(
        this AgentEngineBuilder builder,
        IChatClient chatClient,
        AiOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(chatClient);

        options ??= new AiOptions();
        var brain     = EffectiveBrainOptions(options, options.Brain);
        var extractor = EffectiveExtractorOptions(options, options.Extractor);
        var resolver  = EffectiveResolverOptions(options, options.Resolver);

        // Brain is always wired (agent is useless without one).
        builder.WithLlmBrain(chatClient, brain);

        return options.Policy switch
        {
            AiPolicyMode.Recommended    => builder
                                              .WithLlmExtractor(chatClient, extractor)
                                              .WithLlmActionResolver(chatClient, resolver),
            AiPolicyMode.LlmPrimary     => builder
                                              .WithLlmExtractor(chatClient, extractor)
                                              .WithLlmActionResolver(chatClient, resolver),
            AiPolicyMode.ExtractionOnly => builder
                                              .WithLlmExtractor(chatClient, extractor),
            AiPolicyMode.None           => builder, // brain still wired; nothing else.
            _ => throw new ArgumentOutOfRangeException(nameof(options))
        };
    }

    // ... EffectiveBrainOptions / EffectiveExtractorOptions / EffectiveResolverOptions
    //     same merge pattern as the scraper side.
}
```

### Consumer-facing reduction

```csharp
// scraper — was 5 lines; becomes 1:
var engine = await ScraperEngineBuilder
    .Crawl("https://shop.com").Extract(schema)
    .UseAi(client)              // ← all five.
    .WriteToConsole()
    .BuildAsync();

// agent — was 4 lines; becomes 1:
var engine = await AgentEngineBuilder
    .Start("https://shop.com", "get all products")
    .UseAi(client)              // ← brain + extractor + resolver.
    .BuildAsync();

// agent with overrides — still concise:
var engine = await AgentEngineBuilder
    .Start("https://shop.com", "get all products")
    .UseAi(client, new AiOptions(Temperature: 0.2f, Policy: AiPolicyMode.LlmPrimary))
    .BuildAsync();
```

### Bounded scope (v1)

- **No automatic page-cache wiring.** `.UseAi()` does not call
  `.WithMaxAge(...)`. Caching is orthogonal (a deterministic crawl
  benefits as much as an AI one); the policy stays scoped.
- **No automatic schema validator wiring.** ADR-0062's seam ships
  in this same release; the `LlmSchemaValidator` (the LLM impl) is a
  v2 deferral. When it lands, `.UseAi(...)` will gain a `Validator`
  field in `AiOptions` and wire it on `Recommended` / `LlmPrimary`.
- **No `Custom` mode for à la carte composition.** The `None` arm is
  the explicit escape — wires the brain (on agent) and otherwise
  nothing; the consumer continues with their own `WithLlm*` calls.
  Adding more modes is a v2 question.
- **No multiple chat clients.** `.UseAi(client)` registers the same
  client for every role. A consumer wanting one model for the brain
  and another for the extractor calls `.UseAi(brainClient)`, then
  `.WithLlmExtractor(extractorClient)` à la carte to override.

## Considered options

### Fork 1 — One method vs. two (per-builder)

| Option | What | Verdict |
|---|---|---|
| (a) Two methods — one per builder | `ScraperEngineBuilder.UseAi` and `AgentEngineBuilder.UseAi`. | **Recommended.** Each builder configures different seams (scraper has no brain; agent has no fallback-router); sharing one method would either misleadingly accept agent-only options on the scraper or wire incorrect roles on the wrong builder. |
| (b) One method — generic over the builder type | `BaseBuilder<T>.UseAi(client, opts)`. | Rejected. The builders share no base class; introducing one would re-shape ADR-0009 / ADR-0025 / ADR-0051 for a sugar method. Not worth it. |
| (c) One method on `IChatClient` extensions | `client.For(scraperBuilder)` / `client.For(agentBuilder)`. | Rejected. Inverts the discovery direction (consumer reads "what's on the builder?" naturally, not "what's on the client?"); confuses the builder pattern. |

### Fork 2 — `AiPolicyMode` enum vs. flags

| Option | What | Verdict |
|---|---|---|
| (a) Enum — mutually exclusive | One mode at a time. | **Recommended.** Each canned configuration is a distinct *recommendation* (Recommended is fall-back-first; LlmPrimary is LLM-first). Composing them via flags ("Recommended + LlmPrimary") would be incoherent — the modes mean different things. |
| (b) `[Flags]` enum | `AiFeatures.Extractor | AiFeatures.Resolver | AiFeatures.SelfHealing`. | Rejected. À la carte is exactly what `WithLlm*` already offers; the policy enum exists to be a *canned* configuration. Flags would re-create the à la carte at a slightly different layer. |
| (c) Per-role booleans on options | `AiOptions(WireExtractor: true, WireResolver: true, ...)`. | Rejected. Same — à la carte under a different name. The policy concept is the canned configuration. |

### Fork 3 — Default policy

| Option | What | Verdict |
|---|---|---|
| (a) `Recommended` (deterministic-first) | The cheaper path; LLM as rescue. | **Recommended.** Matches the AI-native wave's structural posture (ADR-0046 router, ADR-0047 self-heal — deterministic-as-decider, LLM-as-proposer). LLM-primary is a deliberate cost choice; defaulting to it would surprise consumers. |
| (b) `LlmPrimary` | LLM does extraction every time; deterministic as a future fallback. | Rejected. Defaults the consumer into the expensive path; surprise on the first invoice. |
| (c) `None` — explicit-only | Consumer always names the mode. | Rejected. Defeats the one-line sugar's purpose. |

### Fork 4 — Should `.UseAi(client)` (no options) be allowed

| Option | What | Verdict |
|---|---|---|
| (a) Yes; all defaults | `.UseAi(client)` = `.UseAi(client, new AiOptions())`. | **Recommended.** The headline one-liner; matches the firecrawl shape. |
| (b) No; require the options bag | `.UseAi(client, new AiOptions(...))` always. | Rejected. The "I trust the recommended defaults" path is the high-frequency case. |

### Fork 5 — Per-role overrides shape

| Option | What | Verdict |
|---|---|---|
| (a) Record-with-nested-records; per-role fields nullable | `AiOptions(Temperature, Extractor: { Temperature })` — global flows down; per-role overrides per field. | **Recommended.** Matches `LlmAgentBrainOptions` per-field-nullable style; merging is explicit (the `Effective*` helpers). |
| (b) Master options bag with no per-role | `AiOptions(Model, Temperature, MaxTokens)` — same everywhere. | Rejected. Per-role caps are real (the resolver's 512-token cap differs from the extractor's 4096); a flat bag forces every role to the same defaults. |
| (c) Per-role options bags only — no global | `AiOptions(Extractor: { ... }, Resolver: { ... }, Brain: { ... })` — explicit per-role. | Rejected. Repeats the same `Model` / `Temperature` three times for the common case where they're the same; defeats the consolidation purpose. |

### Fork 6 — Action resolver registration in `Recommended`

| Option | What | Verdict |
|---|---|---|
| (a) Yes — wire the resolver by default in `Recommended` / `LlmPrimary` | The action resolver is useless without an LLM; a consumer calling `.UseAi(...)` is opting in to AI everywhere. | **Recommended.** Matches the policy's purpose — "make AI work." A consumer who doesn't want SemanticAct picks `ExtractionOnly` mode. |
| (b) No — never wire the resolver via `.UseAi(...)` | The resolver is a SemanticAct-specific seam; auto-wiring it is opinionated. | Rejected. The opinion is the policy's *whole point* — a consumer who needs SemanticAct working without thinking about it shouldn't have to discover `WithLlmActionResolver`. |

### Fork 7 — Should `.UseAi(...)` also call `.WithMaxAge(...)`

| Option | What | Verdict |
|---|---|---|
| (a) No — caching is orthogonal | Page caching benefits deterministic crawls as much as AI; the policy stays scoped. | **Recommended.** Mixing two policies under one method confuses the consumer about what they're opting into. The `WithMaxAge` call is one extra line and explicit. |
| (b) Yes — `AiOptions(MaxAge: TimeSpan.FromHours(1))` | Cache by default in the AI policy. | Rejected. Aggregates two orthogonal policies. A consumer wanting caching adds `.WithMaxAge(...)` after `.UseAi(...)`; explicit beats magic. |

### Fork 8 — `AiPolicyMode.None` for the explicit escape

| Option | What | Verdict |
|---|---|---|
| (a) Keep `None` as the explicit escape hatch | A consumer wanting a bespoke configuration calls `.UseAi(client, opts with { Policy = None })` to register the chat client (via the agent's brain in agent-side) and then composes à la carte. | **Recommended.** Useful for tests (stub the client; wire only the brain) and for bespoke configurations. The name is self-documenting. |
| (b) Drop `None`; consumers skip `.UseAi(...)` entirely if they want à la carte | Three modes only. | Rejected. The agent-side `.UseAi(client, opts with { Policy = None })` is the explicit "register the brain, nothing else" path — useful enough to keep. |

## Consequences

- **The five-method ceremony collapses to one.** `.UseAi(client)` is
  the headline one-liner. The à la carte methods stay for fine-
  tuning — same surface, additive sugar.
- **The firecrawl-shaped AI-on default exists.** "Use AI as
  fallback + self-heal + resolver" is one method call, not three;
  the satellite ships a *canonical* recommended configuration.
- **The agent vs. scraper asymmetry surfaces in the policy modes,
  not in the discovery surface.** The scraper's `Recommended`
  doesn't wire a brain (the scraper has none); the agent's
  `Recommended` always wires the brain (the agent needs one). Same
  method name, semantically-honest per-builder behaviour.
- **Per-role overrides remain.** `AiOptions(Extractor: new
  LlmExtractorOptions(Temperature: 0.5f))` works; global defaults
  flow down per role; per-role wins on conflict.
- **No breakage.** All existing `WithLlm*` methods stay; existing
  consumers see no change. The new sugar is additive.
- **`AiPolicyMode.None` is the test escape.** Stubs the client + the
  brain (agent-side) and wires nothing else; useful for tests and
  bespoke combinations.
- **CONTEXT.md** gains **AI policy** + **AI policy mode** terms in
  the AI-native section.
- **CLAUDE.md** gets a one-line gotcha — `.UseAi(client, opts?)` is
  the headline one-line AI enablement; `WithLlm*` methods remain à
  la carte; default mode is `Recommended` (deterministic-first +
  LLM rescue), not `LlmPrimary`.

## Bounded scope (v1)

- **No `Custom` policy mode** — `None` is the explicit-only escape.
- **No multi-client wiring** — same client per `.UseAi(...)`;
  consumers override per role with à la carte.
- **No automatic page-cache** — `.WithMaxAge(...)` stays orthogonal.
- **No automatic schema validator** — wires when ADR-0062's LLM
  validator ships in a later release.
- **No telemetry / metrics hook** — the policy is wiring, not
  observability. ADR-0059's per-role `LlmCall<T>` logger covers
  per-role telemetry; aggregate telemetry is a future ADR if it
  earns its keep.

## Implementation (slice, when accepted)

**Satellite — new options + two extensions:**

1. **`WebReaper.AI/AiOptions.cs`** — public record + enum.
2. **`WebReaper.AI/LlmRegistration.cs`** — `UseAi` extension on
   `ScraperEngineBuilder`. Contains the `Effective*` merge helpers.
3. **`WebReaper.AI/LlmAgentRegistration.cs`** — `UseAi` extension on
   `AgentEngineBuilder`. Reuses the merge helpers from above
   (refactor: `LlmOptionMerging` static class shared between the two
   files).

**Existing per-role `*Registration` files stay unchanged.** The new
files compose them.

**Tests:**

4. **`WebReaper.Tests/WebReaper.AI.Tests/UseAiTests.cs`** — pin every
   mode's per-builder wiring:
   - Scraper `Recommended` wires fallback + self-heal + resolver.
   - Scraper `LlmPrimary` wires extractor (replacing fold) + self-heal + resolver.
   - Scraper `ExtractionOnly` wires fallback only.
   - Scraper `None` wires nothing.
   - Agent `Recommended` wires brain + extractor + resolver.
   - Agent `LlmPrimary` wires brain + extractor + resolver.
   - Agent `ExtractionOnly` wires brain + extractor only.
   - Agent `None` wires brain only.
   - Per-role override: global `Temperature: 0.2f`, per-role
     `Extractor.Temperature: 0.5f` → extractor gets 0.5, resolver
     gets 0.2.
   - Per-role override: global `Model: "gpt-4o"`, per-role
     `Extractor.Model: "gpt-4o-mini"` → extractor uses mini.
   - Null client / null builder throws.

5. **`WebReaper.Tests/WebReaper.AI.Tests/UseAiBuilderWiringTests.cs`** —
   integration test: builds a scraper engine via `.UseAi(stubClient)`
   and asserts the constructed extractor is an `ExtractionRouter`
   wrapping `SelfHealingContentExtractor` wrapping `LlmContentExtractor`
   (or similar shape, per the policy).

**Docs:**

6. **CONTEXT.md** — new **AI policy** + **AI policy mode** terms;
   relationships line linking policy → roles.
7. **CLAUDE.md** — gotcha line on `.UseAi(client, opts?)` as the
   headline; `WithLlm*` remains à la carte; default mode is
   `Recommended`.

### Guardrails (when implemented)

- `dotnet build WebReaper.sln` — 0 errors.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — all pass (no
  core surface touched).
- `dotnet test WebReaper.Tests/WebReaper.AI.Tests` — all existing
  tests pass; new `UseAiTests` + `UseAiBuilderWiringTests` pass.
- `WebReaper.AotSmokeTest` — unchanged (everything in the non-AOT
  satellite).

## References

- ADR-0009 — registration seam + satellite pattern; the `.UseAi(...)`
  method is one more entry on the seam.
- ADR-0044 — LLM extractor; `WithLlmExtractor` is one of the methods
  `.UseAi(...)` wraps.
- ADR-0046 — extraction router; `WithLlmFallback` wraps the LLM
  extractor as the fallback; `.UseAi(Recommended)` wires this path.
- ADR-0047 — self-healing selectors; `WithLlmSelfHealing` is one of
  the methods.
- ADR-0050 — semantic page actions; `WithLlmActionResolver` is one
  of the methods.
- ADR-0051 — agent driver; `WithLlmBrain` is the agent-side required
  method.
- ADR-0059 — `LlmCall<TResponse>`; the mechanism the wired adapters
  all use under the hood.
- ADR-0060 — tool-calling brain + resolver; `.UseAi(...)` doesn't
  change shape, but the resolved adapters now use tool-calling.
- ADR-0061 — `LastDecisionOutcome`; orthogonal; `.UseAi(...)` doesn't
  touch the agent's state shape.
- ADR-0062 — `ISchemaValidator` seam; the future LLM validator wires
  through `.UseAi(...)` when it ships.
- ADR-0063 — `HtmlToMarkdown` primitive; orthogonal; the wired
  adapters all use it internally.
