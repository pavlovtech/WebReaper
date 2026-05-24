# `IAgentBrain` + `AgentEngine` — autonomous "give me X about this site" loop; LLM picks page, action, schema, stop

## Status

**Proposed — design pass only** (2026-05-24). Generalises the proposer-validator
pattern across the fourth surface — *page selection*. ADR-0046 (router) and
ADR-0047 (self-heal) put it on **extraction**; ADR-0050 put it on **actions**;
this puts it on **which page to look at next**. No implementation in this ADR;
the proposal lives or dies on the design call.

## Context

The self-heal pattern shipped on three surfaces in the 10.0.0 wave:

| Surface | Pattern | ADR |
|---|---|---|
| Extraction (router) | LLM proposes record on deterministic failure; record is validated; cached | ADR-0046 |
| Extraction (self-heal) | LLM proposes selectors; fold validates; patched schema cached | ADR-0047 |
| Actions (semantic) | LLM proposes concrete arm; dispatch validates; arm cached | ADR-0050 |

All three are **page-local**: they decide what to do *on* a page the
caller already chose. The Crawl chain (ADR-0001) decides *which* page to
visit, statically — `Follow("a.product-link")` is a CSS selector chosen
by the caller at build time.

The missing pattern is **page-spanning**: "give me X about this site,
autonomously" — the caller declares a *goal*, not a selector chain. An
agent decides which links to follow (the LLM-flavoured `Follow`), what
to extract (a Schema it proposes or evolves), when it has enough
(termination it decides), and what page actions to run (composes with
ADR-0050's `SemanticAct`).

This is the shape browser-use, Stagehand's higher-level workflows, and
the firecrawl `/scrape?prompt=...` endpoint all converge on. The
proposer-validator discipline carries over: every decision is
deterministic-validated (the page exists, the schema is fold-valid, the
action dispatches successfully), the deterministic path is the hot path
for the inner work; the LLM only steers.

This ADR establishes:

1. **`IAgentBrain`** — the new seam: given the agent's state, return the
   next decision.
2. **`AgentDecision`** — the closed sum of what the brain may return:
   `Extract` / `Follow` / `Act` / `Stop`.
3. **`AgentEngine`** — the alternate driver: same `Spider`, same
   `IScheduler`, same `IScraperSink`s; different driver loop (decide ⇒
   execute ⇒ repeat).
4. **`AgentEngineBuilder`** — a sibling builder to
   `ScraperEngineBuilder` (the two-seam pattern from ADR-0009 /
   ADR-0025); two-line one-liner via static `Agent.Run` sugar.
5. **`WebReaper.AI.LlmAgentBrain`** — the LLM-backed default brain,
   bound to `Microsoft.Extensions.AI`'s `IChatClient`.
6. **`IAgentRunStore`** — durable agent run state seam (HITL flip
   2026-05-24); sibling to `IScheduler` / `IVisitedLinkTracker` /
   `IScraperConfigStorage` / `ICookiesStorage`. InMemory default +
   File adapter in core; Redis / Mongo / Sqlite / Cosmos satellites
   in lockstep.

### Why this is a fourth dock, not a recolour of an existing one

- **Not the Crawl driver with extra knobs.** The Crawl driver (ADR-0022)
  is *declarative*: a fixed selector chain, parallel execution, every
  reached page treated identically. The agent driver is *iterative*: one
  step at a time, the next step depends on the last one, parallelism is
  off by default (the LLM is the bottleneck and the decisions are
  sequential).
- **Not a sink.** Sinks (ADR-0038 — pipeline; ADR-0006 — terminal)
  consume extracted records. The agent decides *what to extract in the
  first place.*
- **Not a content-extractor adapter.** `IContentExtractor` (ADR-0039)
  turns one page's content into one record. The agent spans pages,
  decides whether to load the next one, accumulates across pages.
- **Not a page-action resolver.** ADR-0050's `IActionResolver` resolves
  *one* `SemanticAct` arm to a concrete arm. The agent decides whether to
  perform an action at all — and which one — across the page lifecycle.
  (The agent's `Act` decision *uses* an action resolver under the hood
  when its action is a `SemanticAct`.)

### Composition with existing seams

The agent reuses everything the Crawl driver reuses; the brain replaces
the selector chain as the next-step authority:

| What | Source |
|---|---|
| Page loading | `IPageLoader` (ADR-0004) — HTTP or Puppeteer |
| Page caching | `IPageCache` (ADR-0041) |
| Content extraction (when `Extract` decided) | `IContentExtractor` (ADR-0039) — typically `LlmContentExtractor` (ADR-0044) but anything works |
| Selector repair | `ISelectorRepairer` (ADR-0047) |
| Page actions (when `Act` decided) | the `PageAction` closed sum + `IActionResolver` (ADR-0035 + ADR-0050) for `SemanticAct` |
| Result fan-out | `IScraperSink` (ADR-0006/0038) |
| Page processing | `IPageProcessor` (ADR-0038) |
| Visited-link idempotency | `IVisitedLinkTracker` (ADR-0022) — the agent honours it (no re-loading visited URLs) |
| Cookies / proxies / retry | unchanged |

The agent driver also has access to `MapAsync` (ADR-0042) — the brain
can call it to seed its candidate URL pool from a sitemap before
deciding the first `Follow`. (The brain is just a strategy; it can do
whatever it wants. The seam doesn't require Map use; v1's
`LlmAgentBrain` does it as the first step on the start URL.)

## Decision

Five pieces — one new seam, one closed sum, one driver type, one builder,
one satellite implementation.

### 1. `AgentDecision` — the closed sum the brain returns each step

Lives in **`WebReaper/Domain/Agent/AgentDecision.cs`** (new namespace).
Four nested sealed-record arms on a private-ctor abstract record, same
shape as ADR-0001's `CrawlOutcome` and ADR-0035's `PageAction`:

```csharp
public abstract record AgentDecision
{
    private AgentDecision() { }

    /// <summary>Extract a record from the current page with this Schema.
    /// The brain may evolve the Schema across steps.</summary>
    public sealed record Extract(Schema Schema, string Reason) : AgentDecision;

    /// <summary>Load this URL as the next agent step.</summary>
    public sealed record Follow(string Url, string Reason) : AgentDecision;

    /// <summary>Perform a page action on the current page (then re-ask
    /// the brain on the post-action page state). Composes with ADR-0050:
    /// the action may itself be a SemanticAct.</summary>
    public sealed record Act(PageAction Action, string Reason) : AgentDecision;

    /// <summary>Terminate the agent run. The accumulated records are
    /// the final result.</summary>
    public sealed record Stop(string Reason) : AgentDecision;
}
```

Each arm carries a `Reason` string — the brain's rationale. Surfaced in
the run log and in the final `AgentResult` so the run is *audit-trail
clean* without an external observability hook.

### 2. `IAgentBrain` — the new seam

Lives in **`WebReaper/Core/Agent/Abstract/IAgentBrain.cs`** (new
directory). Public interface, one method:

```csharp
public interface IAgentBrain
{
    ValueTask<AgentDecision> DecideAsync(
        AgentState state,
        CancellationToken cancellationToken = default);
}
```

`AgentState` (public record, immutable) carries everything the brain
needs to decide:

```csharp
public sealed record AgentState(
    string Goal,
    string CurrentUrl,
    string CurrentPageMarkdown,            // pre-cleaned via ADR-0040
    IReadOnlyList<string> CandidateUrls,   // <a> hrefs from the current page
    IReadOnlyList<JsonObject> Extracted,   // records pulled so far
    IReadOnlyList<AgentDecision> History,  // last N decisions
    IReadOnlyList<string> VisitedUrls,     // last N visited URLs
    int StepNumber);
```

The brain receives a *bounded* view (last N entries; `N=10` default; see
fork 3). Token cost is the constraint.

### 3. `AgentEngine` — the alternate driver

Lives in **`WebReaper/Core/Agent/Concrete/AgentEngine.cs`** (internal).
Same dependencies as `ScraperEngine` (one `ISpider`, one `IScheduler`,
sinks, processors, visited-link tracker), plus the `IAgentBrain` and
`AgentEngineOptions`. The driver loop is **sequential** (parallelism =
1 — the LLM is the bottleneck and decisions are causal):

```text
resolve runId (caller-supplied via .WithRunId, or auto-generated)
load snapshot from IAgentRunStore (resume if found; fresh if not)
if resuming: restore History/VisitedUrls/Records/CurrentUrl;
             seed scheduler with CurrentUrl; step := LastDecidedStep
else:        seed scheduler with the start URL; step := 0
loop:
  fetch the current job from the scheduler        (drained ⇒ stop)
  if step >= MaxSteps                              ⇒ stop (cap)
  if cumulative LLM tokens >= MaxBudget (if set)   ⇒ stop (cap)
  load the page via Spider.CrawlAsync (visited-link tracker honoured)
  build the AgentState snapshot
  decision := await brain.DecideAsync(state, ct)
  record the decision in history
  await store.SaveStepAsync(runId, decision, postState, ct)  // PERSIST FIRST
  switch decision:
    Extract(schema, _)   → run schema through registered IContentExtractor;
                           record → page-processor pipeline → sinks; step++
    Follow(url, _)       → enqueue url, dequeue next; step++
    Act(action, _)       → execute action via the Puppeteer transport
                           (PageAction sum dispatched the ADR-0035 way),
                           re-load the post-action page; step++
    Stop(_)              → break
  loop
return AgentResult { RunId, Records, TerminationReason, History, ... }
```

Termination conditions, ordered by precedence:

1. Brain returns `Stop`.
2. Scheduler drained (no more URLs to load).
3. `MaxSteps` reached.
4. `MaxBudgetTokens` reached (cumulative across brain calls;
   off by default).
5. `OperationCanceledException` (caller's `CancellationToken`).

`AgentResult` is the return value (records the brain extracted, the
termination reason, the decision history):

```csharp
public sealed record AgentResult(
    string RunId,
    IReadOnlyList<JsonObject> Records,
    string TerminationReason,
    IReadOnlyList<AgentDecision> History,
    IReadOnlyList<string> VisitedUrls,
    int StepsExecuted);
```

### 4. `AgentEngineBuilder` — the sibling builder

Lives in **`WebReaper/Builders/AgentEngineBuilder.cs`** (public, sibling
to `ScraperEngineBuilder` and `DistributedSpiderBuilder` — the same
two-seam pattern ADR-0025 established).

```csharp
public class AgentEngineBuilder
{
    public static AgentEngineBuilder Start(string startUrl, string goal);

    public AgentEngineBuilder WithBrain(IAgentBrain brain);
    public AgentEngineBuilder WithMaxSteps(int max);          // default 50
    public AgentEngineBuilder WithMaxBudgetTokens(int? max);  // default null (off)
    public AgentEngineBuilder WithBrowser();                  // dynamic-page agent
    public AgentEngineBuilder WithContentExtractor(IContentExtractor extractor);
    public AgentEngineBuilder WithActionResolver(IActionResolver resolver);
    public AgentEngineBuilder WithPageCache(IPageCache cache);
    public AgentEngineBuilder AddSink(IScraperSink sink);
    public AgentEngineBuilder WriteToConsole();
    public AgentEngineBuilder WriteToJsonFile(string path, bool cleanupOnStart = true);
    public AgentEngineBuilder LogToConsole();
    public AgentEngineBuilder WithLogger(ILogger logger);
    // ... satellite extensions add WithRedis*/MongoDb*/Sqlite* here via
    // the same this-extension pattern (ADR-0009).

    public Task<AgentEngine> BuildAsync();
}
```

Plus a `static Agent.Run` sugar for the one-line common case:

```csharp
public static class Agent
{
    public static Task<AgentResult> RunAsync(
        string startUrl,
        string goal,
        IAgentBrain brain,
        Action<AgentEngineBuilder>? configure = null,
        CancellationToken cancellationToken = default);
}
```

### 5. `WebReaper.AI.LlmAgentBrain` — the satellite implementation

Lives in **`WebReaper.AI/LlmAgentBrain.cs`**. Implements `IAgentBrain`
via `IChatClient`. Prompts the LLM with the `AgentState` (serialized as
a compact JSON structure — goal, current page, candidates, history) and
parses the response as one of the four `AgentDecision` arms.

Sister to `LlmContentExtractor` (ADR-0044), `LlmSelectorRepairer`
(ADR-0047), and `LlmActionResolver` (ADR-0050). Same options shape:

```csharp
public sealed record LlmAgentBrainOptions(
    string? Model = null,
    float Temperature = 0.0f,
    int MaxResponseTokens = 1024,
    int MaxPageMarkdownChars = 32_000,
    int HistoryWindow = 10,         // last N decisions kept in the prompt
    int VisitedWindow = 30,         // last N visited URLs kept in the prompt
    int CandidateUrlCap = 50,       // top N candidate URLs from the page
    string? SystemPrompt = null);
```

The satellite registration extension:

```csharp
public static class LlmAgentBrainRegistration
{
    public static AgentEngineBuilder WithLlmBrain(
        this AgentEngineBuilder builder,
        IChatClient chatClient,
        LlmAgentBrainOptions? options = null);
}
```

Plus the maximally-AI-first satellite sugar — the firecrawl-shaped one-liner
the AI-first headline reaches for. Sibling to core `Agent.RunAsync`; this
overload accepts an `IChatClient` directly and constructs the
`LlmAgentBrain` itself, so the caller never names a brain type:

```csharp
public static class LlmAgent
{
    public static Task<AgentResult> RunAsync(
        string startUrl,
        string goal,
        IChatClient chatClient,
        LlmAgentBrainOptions? brainOptions = null,
        Action<AgentEngineBuilder>? configure = null,
        CancellationToken cancellationToken = default);
}
```

Usage: `await LlmAgent.RunAsync("https://shop.com", "get all products", openAiClient);`
— three args, zero ceremony. Lives in **`WebReaper.AI/LlmAgent.cs`** (the
satellite, not core, since `IChatClient` is the LLM dep ADR-0009 quarantines).

The default registration on `AgentEngineBuilder` when no brain is
supplied is **`NullAgentBrain`** (internal singleton, returns
`Stop("no IAgentBrain registered")` — fails fast at the first step,
not at construction; the structural guarantee is on the builder:
`BuildAsync()` throws `InvalidOperationException` when the brain is
still the null one. Same shape as ADR-0050's `NullActionResolver`
warning, sharpened to a throw because the agent is *useless* without a
brain — unlike a Crawl driver, where a SemanticAct without a resolver
might still be a 90%-useful crawl).

### 6. `IAgentRunStore` — durable agent run state (HITL 2026-05-24 flip)

The fourth load-bearing piece of agent state. Sibling to `IScheduler`,
`IVisitedLinkTracker`, `IScraperConfigStorage`, `ICookiesStorage` —
follows the exact same InMemory-default + durable-adapter pattern those
seams use. Pluggable so the agent matches the rest of the library's
distributed-first lineage.

Lives in **`WebReaper/Core/Agent/Abstract/IAgentRunStore.cs`** (public
interface):

```csharp
public interface IAgentRunStore
{
    ValueTask<AgentRunSnapshot?> LoadAsync(
        string runId, CancellationToken cancellationToken = default);

    ValueTask SaveStepAsync(
        string runId,
        AgentDecision decision,
        AgentRunSnapshot postState,
        CancellationToken cancellationToken = default);

    ValueTask DeleteAsync(
        string runId, CancellationToken cancellationToken = default);
}
```

The snapshot — public record in **`WebReaper/Domain/Agent/AgentRunSnapshot.cs`**:

```csharp
public sealed record AgentRunSnapshot(
    string Goal,
    int LastDecidedStep,
    IReadOnlyList<AgentDecision> History,
    IReadOnlyList<string> VisitedUrls,
    IReadOnlyList<JsonObject> Records,
    string? CurrentUrl);
```

**Semantics — persist-before-execute, at-least-once on effects:**

1. Brain returns `decision` for step *N*.
2. Engine calls `SaveStepAsync(runId, decision, postState)` *before* executing.
3. Engine executes (Extract → sinks; Follow → enqueue; Act → page action; Stop → break).
4. On crash mid-execute, resume re-executes step *N*'s effect — sinks may
   see a duplicate record for that step (caller's sink-idempotency concern).
5. On resume, engine loads the snapshot, restores `History`/`VisitedUrls`/
   `Records`/`CurrentUrl`, and starts deciding step *N + 1*.

Brain decisions are exactly-once (history is the load-bearing record; re-
deciding would produce a different `History` and break the run's reasoning).
Effect execution is at-least-once. **New gotcha for CLAUDE.md: "Resumable
agent runs require idempotent sinks; the change-tracking processor (ADR-0048)
deduplicates on hash, so it composes cleanly."**

**Default + built-in adapters (both in v1 core):**
- `InMemoryAgentRunStore` — internal, default. Dictionary keyed by `runId`.
  Per-process; suitable for the firecrawl-shaped one-liner where the call
  is short-lived and awaited.
- `FileAgentRunStore` — internal, registered via
  `.WithFileAgentRunStore(directory)`. JSON Lines per run; one file per
  `runId`. Satisfies ADR-0036's ≥2-adapter rule.

**Satellite adapters (all four in v10, lockstep):**
- `WebReaper.Redis.RedisAgentRunStore` + `.WithRedisAgentRunStore(...)`
- `WebReaper.Mongo.MongoAgentRunStore` + `.WithMongoAgentRunStore(...)`
- `WebReaper.Sqlite.SqliteAgentRunStore` + `.WithSqliteAgentRunStore(...)`
- `WebReaper.Cosmos.CosmosAgentRunStore` + `.WithCosmosAgentRunStore(...)`

`WebReaper.AzureServiceBus` is queue-shaped, doesn't fit a key-value
snapshot store — skipped intentionally.

**Builder additions on `AgentEngineBuilder`:**

```csharp
public AgentEngineBuilder WithRunStore(IAgentRunStore store);
public AgentEngineBuilder WithRunId(string runId);   // resume if saved
public AgentEngineBuilder WithNewRun();              // force fresh runId
public AgentEngineBuilder WithFileAgentRunStore(string directory);
// + satellite extensions WithRedis/Mongo/Sqlite/Cosmos as above
```

**Resume default:** calling `.WithRunId("foo")` with a saved snapshot
resumes from `LastDecidedStep + 1`. Fresh runs use a new `runId` or
`.WithNewRun()` (engine auto-generates a `runId` if neither set). Matches
Firecrawl's `jobId` model — caller holds the run identifier; the store
holds the state.

**Static-sugar overloads:**

```csharp
// Core (Agent.cs):
public static Task<AgentResult> Agent.RunAsync(
    string startUrl, string goal, IAgentBrain brain,
    Action<AgentEngineBuilder>? configure = null,
    CancellationToken ct = default);

public static Task<AgentResult> Agent.ResumeAsync(
    string runId, IAgentBrain brain, IAgentRunStore store,
    Action<AgentEngineBuilder>? configure = null,
    CancellationToken ct = default);

// Satellite (LlmAgent.cs):
public static Task<AgentResult> LlmAgent.RunAsync(
    string startUrl, string goal, IChatClient chatClient, ...);

public static Task<AgentResult> LlmAgent.ResumeAsync(
    string runId, IChatClient chatClient, IAgentRunStore store, ...);
```

`AgentResult` gains a `string RunId` field so the caller can resume later.

## Considered options (per fork)

### Fork 1 — `AgentDecision` arm count: three vs four

| Option | What | Verdict |
|---|---|---|
| (a) Extract / Follow / Stop | Three arms. Page actions ride as part of `Follow` (`Follow(url, actions: [...])`). | Rejected. Conflates "load this page" with "do something to the current page." Click-through forms ("sign in then scrape") become a separate Follow with the action attached, but the action runs *on the loaded page*, not on the current page — exactly the opposite of the intent. |
| (b) Extract / Follow / Act / Stop | **Recommended.** Four arms. `Act` operates on the current page; post-action the brain is re-asked. | Maps cleanly to the actual use case: "click sign in" then "extract the dashboard" — two decisions on the same URL, in order. |
| (c) Extract / Navigate / Stop (Navigate = Follow + actions in one) | Three arms; Navigate carries both the URL and the action list. | Rejected. Forces the brain to know the action list *before* loading the page — pre-decided. The Stagehand pattern is "see, then act" (post-load); pre-decided actions are the Crawl driver's job. |

### Fork 2 — goal representation: string vs structured

| Option | What | Verdict |
|---|---|---|
| (a) Plain string | `"get all product names and prices"`. | **Recommended.** Same discipline as ADR-0050 (intent string), ADR-0044 (no goal at all). Structured goal is a v2 specialisation once a real caller surfaces it. |
| (b) `AgentGoal { Intent, MaxRecords, RequiredFields, ... }` | Structured. | Rejected (deferred to v2). Speculative generality; no caller has named the fields. ADR-0036's discipline: "shape from the second adapter, not for it." |
| (c) `Func<AgentState, bool> isSatisfied` | A goal-completion predicate the engine evaluates after each Extract. | Rejected. The brain already decides Stop. A separate predicate is a second authority; the brain can read its own records and decide. (Could be a deferred enhancement: `IAgentGoalValidator` as a v2 seam.) |

### Fork 3 — state passed to brain: bounded vs full

| Option | What | Verdict |
|---|---|---|
| (a) Capped history (last N decisions, last N visited URLs) | **Recommended.** N defaults — see `LlmAgentBrainOptions`. | Token cost; long-running agents would unbounded the prompt. Same shape as ADR-0044's `MaxTokens` and ADR-0050's `MaxHtmlChars`. |
| (b) Full history | Everything. | Rejected. Unbounded prompt; first cause of cost runaway on long runs. |
| (c) Summarised history | LLM compresses history every N steps. | Rejected (deferred to v2). A second LLM seam; adds latency and a token spike. Worth it only if (a) proves insufficient on real workloads. |

### Fork 4 — schema authority: brain-chosen vs pre-fixed

| Option | What | Verdict |
|---|---|---|
| (a) Brain returns Schema with each Extract | **Recommended.** The brain can evolve the schema as it learns the site. | Matches the autonomous-goal use case ("get all product details" — the brain figures out what fields). Pre-fixed is a v2 specialisation. |
| (b) User pre-fixes Schema via `.WithSchema(schema)`; brain only decides *when* to Extract | The Schema is invariant across the run. | Rejected (deferred to v2). A specialisation of (a) — the brain just returns the pre-fixed Schema every time. Easy to add later as `.WithSchema(schema)`. |
| (c) Brain optional; infer from goal | If no brain supplied, the engine infers a schema from the goal string. | Rejected. Too magical, breaks the seam discipline (engine starts doing LLM work itself). The brain is the right home for inference. |

### Fork 5 — builder entry: new builder vs `.AsAgent(...)` on existing

| Option | What | Verdict |
|---|---|---|
| (a) `ScraperEngineBuilder.AsAgent(goal, brain)` | Add a method on the existing builder. | Rejected. `BuildAsync()` returns `ScraperEngine`, not `AgentEngine` — would need a generic terminal or a second `BuildAgentAsync()`. Conflates two driver types on one builder; misleads the consumer about which one they're building. |
| (b) New `AgentEngineBuilder` (sibling) | **Recommended.** The two-seam pattern from ADR-0009 (`DistributedSpiderBuilder`) and ADR-0025 (the structural guarantee lives on the *type*, not on a method choice). | The agent and the crawl driver are genuinely different things; two builders make that explicit. Satellite extensions that target both can `this AgentEngineBuilder` + `this ScraperEngineBuilder` (the in-tree examples — Redis, Mongo, Sqlite — would naturally extend both). |
| (c) New static `Agent.Run(goal, startUrl, brain, options)` only — no builder | One-liner only; no fluent shape. | Rejected as the sole entry. **Kept as sugar over (b).** The one-liner covers the demo case; the builder covers configuration. **Refinement (HITL 2026-05-24):** the satellite adds a second sugar layer — `WebReaper.AI.LlmAgent.RunAsync(startUrl, goal, chatClient, configure?)` — which constructs the `LlmAgentBrain` internally so the firecrawl-shaped caller never touches it. Core `Agent.RunAsync` stays brain-explicit (AOT-clean, no LLM dep); the satellite sugar is where the maximally-AI-first three-arg one-liner lives. |

### Fork 6 — termination: Stop only vs Stop + hard caps

| Option | What | Verdict |
|---|---|---|
| (a) Only Stop decision terminates | Brain is the sole authority. | Rejected. A misbehaving brain (infinite Follows, ignored Stop) and a pathological page (cycle of intents) need hard caps. |
| (b) Stop + max-steps + token-budget + page-crawl-limit + cancellation | **Recommended.** Defence in depth. Defaults: `MaxSteps=50`, `MaxBudgetTokens=null` (off), page-crawl-limit honoured if set. | The defaults are intentionally generous on max-steps (50 is far above the typical run); token budget is off by default because token-counting is per-model and would lock the satellite into a tokeniser dep (ADR-0050 lesson). |

### Fork 7 — budget governance: seam vs options

| Option | What | Verdict |
|---|---|---|
| (a) `IBudget` seam the brain can read | The brain can self-budget. | Rejected (deferred to v2). The brain *shouldn't* know its own budget — it could game it. The engine enforces; brain doesn't know. |
| (b) `AgentEngineOptions` fields, engine enforces | **Recommended.** `MaxSteps`, `MaxBudgetTokens`. No brain visibility. | Smaller surface; brain ignorance is a feature. |

### Fork 8 — state persistence: durable vs in-process

Originally proposed: in-process v1, deferred durable to v2. **Verdict flipped
2026-05-24 after HITL grilling** — the architectural-consistency argument
landed. See "Why the flip" below.

| Option | What | Verdict |
|---|---|---|
| (a) Durable agent state (resumable runs) | `IAgentRunStore` seam; persist the brain's decision before the engine executes it; on resume start at `LastDecidedStep + 1`. Effects re-run at-least-once. | **Recommended (flipped 2026-05-24).** Matches every other WebReaper state seam (`IScheduler`, `IVisitedLinkTracker`, `IScraperConfigStorage`, `ICookiesStorage` are all InMemory + durable adapters); agent state being in-memory-only would be the *one* inconsistent piece of an otherwise distributed-first library. See §Decision §6. |
| (b) In-process only | Start over on restart. `IPageCache` (ADR-0041) covers the page-load amortisation. | Rejected. Was the original recommendation. Two grounded counter-arguments landed: (1) **architectural consistency** — every other seam pluggable durable/in-memory, agent state alone in-memory is glaring; (2) **peer-pattern landscape** is split — Firecrawl persists agent state server-side (`/v2/agent/{jobId}` poll model); Stagehand keeps the agent loop in-process but persists *browser* state via Browserbase Contexts (`context: { id, persist: true }`). WebReaper is distributed-first by lineage (since 7.x), so the Firecrawl shape is the closer fit. |

**Why the flip:** the original "shape from the second adapter" appeal (ADR-0036)
assumed durability was a *future* differentiator. Grounding showed it is a
*current* one — the AI-native pitch for a .NET enterprise library means
"agent state survives `kubectl rollout restart`," and a v10 that ships an
in-memory-only agent driver alongside fully-durable crawl state is the
inconsistency callers will notice first. Breaking-change OK for v10
(this *is* the AI-native version cut).

### Fork 9 — result aggregation: per-Extract vs final-record

| Option | What | Verdict |
|---|---|---|
| (a) Each Extract emits to the existing sink fan-out (one record per Extract) | **Recommended.** Composes with the existing sinks unchanged. | Aggregation, if needed, is a sink wrapper. Same discipline as ADR-0040 (one record per page). |
| (b) Engine aggregates into one final result; sinks emit once at the end | The final `AgentResult.Records` is the sink payload. | Rejected. Breaks the per-record sink model; loses ordering and the change-tracking (ADR-0048) hook. |

### Fork 10 — validation: brain self-evaluates vs explicit `IAgentValidator`

| Option | What | Verdict |
|---|---|---|
| (a) Brain owns validation (decides Stop based on its own count of records) | **Recommended.** Less seam; brain has context. | Matches the v1 discipline. Future explicit `IAgentValidator` is a v2 seam if a real caller surfaces. |
| (b) Engine evaluates via an explicit `IAgentValidator` seam | After each Extract, the validator returns continue-or-stop. | Rejected (deferred). Speculative seam. Brain is sufficient. |

### Fork 11 — parallelism

| Option | What | Verdict |
|---|---|---|
| (a) Sequential (degree=1) | **Recommended.** One step at a time; brain decisions are causal. | The LLM is the bottleneck, and decisions are *intentionally* sequential — the brain's next decision depends on the last extract. |
| (b) Parallel (degree=N) | Multiple agent steps in flight. | Rejected. Parallel brain decisions are not causally ordered; the brain can't reason about a state it hasn't observed. Per-page parallelism is the Crawl driver's strength — the agent is the opposite by design. |

**Peer-pattern grounding (HITL 2026-05-24).** All three reference peers
run a single agent loop sequentially; parallelism lives at the
multi-agent / multi-session level, not within one agent's steps.
**Firecrawl** — `/v2/agent` runs one agent autonomously server-side;
the client polls a `jobId`. They explicitly price "Parallel Agents"
(Spark-1 Fast, 10 credits per cell) — i.e., run N independent agents
concurrently from the client, not parallel steps within one.
**Browserbase** — explicit `Promise.allSettled` / `asyncio.gather`
patterns for parallel *sessions* with enforced Max Concurrent Browsers
+ Session Creation Limit; no within-agent parallelism (Browserbase
isn't an agent layer). **Stagehand** — `agent.execute({ instruction,
maxSteps: N })` is single-shot; their own docs recommend "break into
smaller tasks with success checking" for complex work (sequential
decomposition). Multi-agent parallelism in Stagehand is achieved by
running multiple Browserbase sessions in parallel. The orthogonal
multi-agent-orchestration axis is captured here as v2 deferral (i).

### Fork 12 — visited-link semantics

| Option | What | Verdict |
|---|---|---|
| (a) Honour the existing `IVisitedLinkTracker`; reject re-Follow of a visited URL | **Recommended.** Same idempotency authority as the Crawl driver (ADR-0022). | The brain may propose a visited URL; the engine declines and lets the brain re-decide. Logged so the brain learns. |
| (b) Allow re-Follow of visited URLs | The brain knows what it's doing. | Rejected. Cost runaway; the brain can re-load the same page on every step. The idempotency invariant is load-bearing. |
| (c) Brain decides — pass the visited list, brain self-polices | Recommended as additional: the `VisitedUrls` field in `AgentState` makes (a) visible to the brain. | This is *and* (a). The brain *sees* what's visited and avoids proposing it; the engine *enforces* by rejecting. Belt + braces. |

## Consequences

- **The proposer-validator pattern now sits on four surfaces.** Page
  selection joins extraction-routing, extraction-self-healing, and
  semantic actions. CONTEXT.md gets a new top-level term: **Agent
  driver** (sibling to the existing **Crawl driver**) — and a new
  Relationships line connecting the four docks.
- **The two-seam builder pattern gets a third instance.**
  `ScraperEngineBuilder` (engine path), `DistributedSpiderBuilder`
  (distributed-worker reduced shell), and now `AgentEngineBuilder`
  (agent path). Three sibling types, each with the structural-guarantee
  property the ADR-0025 pattern protects.
- **The funnel headline expands from "scrape any site declaratively" to
  "+ give me X about any site autonomously".** Matches the
  REPOSITIONING-PLAN's *funnel-then-agent* narrative.
- **AOT-clean by design.** The brain is a seam; the LLM impl is in the
  `WebReaper.AI` satellite (ADR-0009 quarantine). Core gains:
  `AgentDecision` (a closed sum record), `IAgentBrain`, `IAgentRunStore`,
  `AgentEngine`, `AgentEngineBuilder`, `AgentState`, `AgentResult`,
  `AgentRunSnapshot`, `NullAgentBrain`, `InMemoryAgentRunStore`,
  `FileAgentRunStore`. No reflection, no `Microsoft.Extensions.AI`
  dependency in core.
- **Composition with existing pieces is the headline.** The agent reuses
  every loader, cache, extractor, action resolver, sink, processor, and
  visited-link tracker. The two new collaborators the agent demands are
  the **brain** (`IAgentBrain`) and the **run store** (`IAgentRunStore`)
  — the rest is registered exactly the way a `ScraperEngine` would.
  Satellite extensions (`.WithRedis*` / `.WithMongoDb*` / `.WithSqlite*`
  / `.WithCosmos*`) get `this AgentEngineBuilder` overloads in lockstep
  — agent-run-store adapters ride those extensions.
- **The `IContentExtractor` seam (ADR-0039) is the agent's extraction
  authority.** When the brain returns `Extract(schema)`, the engine runs
  the *registered* extractor with that schema. If `WithLlmExtractor` is
  registered, the schema is structured-output; if the default
  `SchemaFold` is registered, the schema is selector-driven. The
  *brain* doesn't extract — the brain *decides*. Same separation
  ADR-0046/0047 made between "deciding" and "doing".
- **The `IActionResolver` seam (ADR-0050) is the agent's action
  authority.** When the brain returns `Act(SemanticAct(intent))`, the
  Puppeteer transport's `SemanticActCoordinator` (ADR-0050) handles it
  exactly as in the Crawl path — cache and all. The agent gets
  selector-caching for free.
- **`IAgentRunStore` makes the agent distributed-first like the rest of
  the library.** (HITL flip 2026-05-24.) Every other piece of crawl
  state — `IScheduler`, `IVisitedLinkTracker`, `IScraperConfigStorage`,
  `ICookiesStorage` — has been InMemory-default + durable adapters
  since 7.x; the agent's run state now matches. v10 ships the seam +
  in-memory default + File adapter + Redis / Mongo / Sqlite / Cosmos
  satellites in lockstep. Resumable agent runs survive process
  restarts; the firecrawl-shaped one-liner caller never sees the
  `runId` unless they ask. **Breaking change for v10** — this is the
  AI-native version cut, and an in-memory-only agent driver alongside
  fully-durable crawl state would be the inconsistency callers notice
  first.

## Bounded scope (v1)

The named v2 deferrals, all surfaced above in the considered-options
tables, collected for the v2-deferral ledger:

- **(a) Structured `AgentGoal`** — string in v1.
- **(b) Summarised history** — capped raw history in v1.
- **(c) Schema-pre-fixed agent** — `.WithSchema(schema)` once a caller asks.
- **(d) `IBudget` seam** — options-only enforcement in v1.
- ~~**(e) Durable agent state** — in-process only in v1; resume is "start over".~~ **Flipped 2026-05-24 — shipped in v1.** `IAgentRunStore` seam + `InMemoryAgentRunStore` default + `FileAgentRunStore` + four satellite adapters (Redis, Mongo, Sqlite, Cosmos). See Fork 8 and §Decision §6.
- **(f) `IAgentValidator` seam** — brain self-evaluates in v1.
- **(g) Parallel agent steps** — sequential in v1 (rejected, may stay rejected).
- **(h) Distributed agent** — agent runs in-process in v1; sharing a
  brain across distributed workers is a different design.
- **(i) Multi-agent orchestration** — agents-of-agents is a v2+ shape
  entirely.
- **(j) Streaming brain decisions** — `IChatClient` supports streaming;
  `DecideAsync` is non-streaming in v1 (the brain's output is a single
  JSON-shaped decision, not a long-form text).
- **(k) Token-counting at the engine** — `MaxBudgetTokens` reads
  `ChatResponse.Usage.TotalTokenCount` when the chat client surfaces it;
  the engine doesn't do its own tokenising. If the underlying chat
  client doesn't report usage, the cap silently does nothing —
  acceptable v1; v2 may surface a warning.

## Implementation (slice, when accepted)

**Core domain + seams:**

1. **`WebReaper/Domain/Agent/AgentDecision.cs`** — closed-sum record.
2. **`WebReaper/Domain/Agent/AgentState.cs`** — public state record.
3. **`WebReaper/Domain/Agent/AgentResult.cs`** — public result record
   (includes `string RunId` so the caller can resume).
4. **`WebReaper/Domain/Agent/AgentRunSnapshot.cs`** — public snapshot
   record persisted by `IAgentRunStore`.
5. **`WebReaper/Core/Agent/Abstract/IAgentBrain.cs`** — brain seam.
6. **`WebReaper/Core/Agent/Abstract/IAgentRunStore.cs`** — durable-state
   seam (HITL flip 2026-05-24; §Decision §6).

**Core implementations:**

7. **`WebReaper/Core/Agent/Concrete/NullAgentBrain.cs`** — internal
   default, returns `Stop`.
8. **`WebReaper/Core/Agent/Concrete/InMemoryAgentRunStore.cs`** —
   internal default `IAgentRunStore`; dictionary keyed by `runId`.
9. **`WebReaper/Core/Agent/Concrete/FileAgentRunStore.cs`** — internal
   File adapter; JSON Lines per run, one file per `runId`. Satisfies
   ADR-0036 ≥2-adapter rule.
10. **`WebReaper/Core/Agent/Concrete/AgentEngine.cs`** — driver loop.
    Persist-before-execute semantics (§Decision §6).
11. **`WebReaper/Core/Agent/Concrete/AgentEngineOptions.cs`** —
    `MaxSteps`, `MaxBudgetTokens`.
12. **`WebReaper/Builders/AgentEngineBuilder.cs`** — public builder.
    Includes `.WithRunStore`, `.WithRunId`, `.WithNewRun`,
    `.WithFileAgentRunStore` alongside `.WithBrain` etc.
13. **`WebReaper/Agent.cs`** — static `Agent.RunAsync(...)` +
    `Agent.ResumeAsync(...)` sugar.

**AI satellite (LLM brain + sugar):**

14. **`WebReaper.AI/LlmAgentBrain.cs`** — `IAgentBrain` via `IChatClient`.
15. **`WebReaper.AI/LlmAgentBrainOptions.cs`** — options record.
16. **`WebReaper.AI/LlmAgentBrainRegistration.cs`** — `WithLlmBrain`.
17. **`WebReaper.AI/LlmAgent.cs`** — satellite static sugar
    `LlmAgent.RunAsync(startUrl, goal, chatClient, ...)` +
    `LlmAgent.ResumeAsync(runId, chatClient, store, ...)`.

**Durable `IAgentRunStore` satellites (all four in v10 lockstep):**

18. **`WebReaper.Redis/RedisAgentRunStore.cs`** + registration
    `WithRedisAgentRunStore(...)`. Snapshot stored as JSON under
    key `webreaper:agent:run:{runId}`.
19. **`WebReaper.Mongo/MongoAgentRunStore.cs`** + registration
    `WithMongoAgentRunStore(...)`. Snapshot as a document in the
    `agent_runs` collection keyed by `runId`.
20. **`WebReaper.Sqlite/SqliteAgentRunStore.cs`** + registration
    `WithSqliteAgentRunStore(...)`. Two-table schema (`runs` header
    + `run_steps` decision log) for transactional persist-before-execute.
21. **`WebReaper.Cosmos/CosmosAgentRunStore.cs`** + registration
    `WithCosmosAgentRunStore(...)`. Snapshot as a document keyed by
    `runId` in the partitioned `AgentRuns` container.

`WebReaper.AzureServiceBus` is queue-shaped, doesn't fit a snapshot
store — intentionally skipped.

**Tests:**

22. **`WebReaper.Tests/WebReaper.UnitTests/AgentEngineDriverTests.cs`**
    — pins driver-loop semantics with a stub brain (every termination
    case, decision dispatch, visited-link enforcement, sink fan-out,
    persist-before-execute, resume from snapshot, `RunId` on result).
23. **`WebReaper.Tests/WebReaper.UnitTests/AgentEngineBuilderTests.cs`**
    — pins builder grammar (start URL required; brain registration
    required for `BuildAsync` success; `WithRunId` resumes from saved
    snapshot, `WithNewRun` forces fresh).
24. **`WebReaper.Tests/WebReaper.UnitTests/AgentRunStoreContractTests.cs`**
    — shared contract test base; every `IAgentRunStore` implementation
    is exercised against it (load-missing returns null, save+load
    round-trips, delete clears, concurrent runIds don't collide).
25. **`WebReaper.Tests/WebReaper.UnitTests/InMemoryAgentRunStoreTests.cs`**
    + **`FileAgentRunStoreTests.cs`** — derive from contract base.
26. **`WebReaper.Tests/WebReaper.AI.Tests/LlmAgentBrainTests.cs`** —
    pins prompt shape, decision parsing, every arm round-trip,
    code-fence stripping (mirror `LlmActionResolverTests`). Includes
    one round-trip for `LlmAgent.RunAsync` confirming it ends-to-ends
    via the satellite sugar.
27. **`WebReaper.Tests/WebReaper.Redis.Tests/RedisAgentRunStoreTests.cs`**,
    **`WebReaper.Tests/WebReaper.Mongo.Tests/MongoAgentRunStoreTests.cs`**,
    **`WebReaper.Tests/WebReaper.Sqlite.Tests/SqliteAgentRunStoreTests.cs`**,
    **`WebReaper.Tests/WebReaper.Cosmos.Tests/CosmosAgentRunStoreTests.cs`**
    — derive from contract base; satellite-specific quirks (Sqlite
    transactional, Cosmos partition key, etc.).

**Docs:**

28. **CONTEXT.md** — new **Agent driver**, **Agent brain**, **Agent
    decision**, **Agent step**, **Agent run state** terms; relationships
    connecting the four proposer-validator docks; note `IAgentRunStore`
    as the agent's pluggable state seam, sibling to `IScheduler` etc.
29. **CLAUDE.md** — extend the AI-native paragraph to `ADR-0040..0051`;
    add seam table row for `IAgentRunStore`; new gotchas
    (brain-required-or-throw, sequential-by-design,
    token-budget-needs-Usage, **resumable-runs-need-idempotent-sinks**).
30. **CHANGELOG.md** — new bullet under the AI-native wave naming
    `AgentEngine` + `AgentEngineBuilder` + `WithLlmBrain` +
    `LlmAgent.RunAsync` + `IAgentRunStore` (InMemory / File / Redis /
    Mongo / Sqlite / Cosmos).

### Guardrails (when implemented)

- `dotnet build WebReaper.sln` — 0 errors.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — all pass.
- `dotnet test WebReaper.Tests/WebReaper.AI.Tests` — all pass.
- `WebReaper.AotSmokeTest` — agent driver added to the AOT-publish
  smoke as a no-LLM smoke (uses a stub `IAgentBrain` that returns
  `Stop` on the first step) — confirms the new core types are AOT-safe.

## References

- ADR-0022 — Crawl driver + Outstanding-work latch; the *other* driver,
  whose architectural shape the agent driver mirrors (in-process,
  same Spider, same scheduler, same sinks).
- ADR-0025 — staged builder entry; the structural-guarantee pattern
  `AgentEngineBuilder` inherits.
- ADR-0009 — satellite adapter + registration seam; the pattern
  `WithLlmBrain` instances.
- ADR-0039 — `IContentExtractor` seam; the agent's extraction
  authority (the brain doesn't extract — it decides).
- ADR-0044 — `WebReaper.AI` LLM extractor satellite; the project the
  brain ships in, the same `IChatClient` binding.
- ADR-0046 — `ExtractionRouter`; the first proposer-validator dock
  (extraction routing).
- ADR-0047 — `SelfHealingContentExtractor`; the second dock
  (extraction self-heal). The brain-decides-Schema-each-step pattern
  is a generalisation of the self-heal cache (which decides Schema
  *once* on failure).
- ADR-0050 — `PageAction.SemanticAct` + `IActionResolver`; the third
  dock (semantic actions). The agent's `Act(SemanticAct(intent))`
  composes with it directly.
- ADR-0042 — `ISiteMapper`; the deterministic URL discovery the brain
  may invoke as its first step.
- ADR-0041 — `IPageCache`; the page-load amortisation the agent reuses
  unchanged.
- ADR-0038 — page-processor pipeline + sink fan-out; the agent reuses
  it for every `Extract` decision's record.
- REPOSITIONING-PLAN §2.1 + §2.4 — the funnel-then-agent narrative
  this ADR cashes.
- *2026-05-24 Browserbase/Stagehand research notes* — the
  observe-then-act + self-heal-via-cache shape the four-dock
  pattern generalises.
