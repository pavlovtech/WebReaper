# `LlmCall<TResponse>` system-prompt caching — descriptor opt-in + cached-token telemetry

## Status

**Accepted — design pass** (2026-05-25). First ADR of the v10.0.0 pre-tag
cost-optimisation slice. Pairs with **ADR-0066** which turns the per-call
`InputTokens` / `OutputTokens` / `CachedInputTokens` fields this ADR adds
into engine-level cost telemetry on both `ScraperEngine.RunAsync` and
`AgentEngine.RunAsync`. Folds into v10.0.0 — the tag waits on this PR.

## Context

After ADR-0059 centralised the four LLM adapters' mechanism in
`LlmCall<TResponse>`, every page of every crawl re-sends the same
system prompt:

| Adapter | System-prompt token cost | Repeated per |
|---|---|---|
| `LlmContentExtractor` | ~1.5 k (instructions + schema-shape description) | every parsed page |
| `LlmSelectorRepairer` | ~1.0 k (repair instructions) | every self-heal trip |
| `LlmActionResolver` | ~0.8 k (resolution instructions + few-shot) | every first-of-intent page |
| `LlmAgentBrain` | ~2.0 k (brain prompt + tool registry — ADR-0060) | every agent step |

A 100-page crawl with the extractor at 1.5 k system tokens × 100 calls
= 150 k input tokens of *identical* content. Every major hosted
provider now supports prompt caching:

- **Anthropic** — explicit `cache_control: { type: "ephemeral" }` on a
  message / content block. 5-minute TTL. Cache write costs ~1.25× a
  normal input token; cache read costs ~0.1× (~90% cheaper). Available
  on Claude 3 / 4.X (Aug 2024, GA late 2024).
- **OpenAI** — automatic; no API change. System + user prefix ≥ 1024
  tokens cached for ~5–10 min. Cache read ~50% cheaper. (Oct 2024.)
- **Google Gemini** — explicit `cachedContent` resource via a separate
  allocation API. TTL configurable. Different surface; out of scope
  this ADR (deferred — different shape entirely).
- **Local models (Ollama, llama.cpp, vLLM)** — no caching model.

The mechanism centralises system-prompt marshalling — it is the right
place to add cache hints. ADR-0051's `MaxBudgetTokens` reads
`TotalTokens` but never sees that 90% of those tokens were charged at
10%; the cost-budget silently overcounts cached reads. **ADR-0066
closes this gap; this ADR is the precondition** — the per-call split is
captured here, the engine-level aggregate lives there.

Three motivations:

- **Cost.** Caching savings dominate every other LLM-cost lever. 1.5 k
  system tokens × 100 pages × $3 / MTok input = $0.45 uncached; cached,
  $0.045. For 1 k-page crawls, $4.50 → $0.45. The 25% cache-write
  premium pays back at the second hit of the same prefix within TTL.
- **Latency.** Cached prompts return faster — ~30% on Anthropic, minor
  on OpenAI. For agent runs (sequential by design — ADR-0051), the
  per-call latency compounds across decisions.
- **Honest telemetry.** Cost budgets that count cached tokens at full
  price overcount by 5–10×. Either we surface the split or
  `MaxBudgetTokens` reports phantom-spend.

### What this ADR does and does not move

**Moves into `LlmCall`:**
- Descriptor opt-in for system-prompt caching (`CachePolicy` enum).
- Mechanism encoding of the cache hint via `AdditionalProperties` on
  the system `ChatMessage` (Anthropic's `cache_control` convention;
  ignored by providers that don't recognise the key).
- Capture of split usage fields — `InputTokenCount`,
  `OutputTokenCount`, `AdditionalCounts["cached_input_tokens"]` (or
  the provider-equivalent key the M.E.AI adapter populates; see
  Implementation §3) — populated on `LlmCallResult` regardless of
  whether caching was hinted. OpenAI auto-caches; we should surface
  that even without an opt-in.
- `AiOptions` gains a `CachePolicy` field; per-role `LlmExtractorOptions`
  / `LlmActionResolverOptions` / `LlmAgentBrainOptions` each gain a
  nullable `CachePolicy?` field; the ADR-0064 `Effective*` helpers
  carry global → per-role.

**Stays out of scope (v1):**
- **User-message prefix caching.** The extractor's schema-JSON prefix
  is invariant per crawl; v2 may add a `UserPromptCachePrefixLength`
  on the descriptor and have the mechanism split the user message at
  the boundary. v1 caches the system prompt only — the highest-value
  fixed prefix.
- **Tool-registry caching.** Anthropic's API supports `cache_control`
  on the tools array; the brain has 9 tools (ADR-0060), the resolver
  has 6. Multi-block cache is a v2 widening.
- **Gemini `cachedContent`.** Different API — a separate allocation
  request that returns a cache resource id passed on subsequent
  calls. Out of scope here; would warrant its own ADR.
- **Per-call cache identity.** The mechanism does not manage cache
  keys. Anthropic / OpenAI compute their own from prompt content;
  the mechanism's job ends at "I hinted."

## Decision

Three pieces in the satellite (`WebReaper.AI`). Core unchanged.

### 1. `CachePolicy` enum (new)

`WebReaper.AI/Llm/CachePolicy.cs`. Public enum:

```csharp
namespace WebReaper.AI.Llm;

/// <summary>
/// Per-role policy for system-prompt caching (ADR-0065). The mechanism
/// (<see cref="LlmCall{TResponse}"/>) encodes the policy as a provider-
/// specific hint on the outbound system <see cref="ChatMessage"/>;
/// providers that recognise the hint cache the prefix, providers that
/// do not silently ignore it.
/// </summary>
public enum CachePolicy
{
    /// <summary>The mechanism does NOT add a provider-specific cache
    /// hint. Providers that auto-cache (OpenAI: stable prefix ≥ 1024
    /// tokens) still cache; providers that need explicit hints
    /// (Anthropic) do not. <see cref="LlmCallResult{TResponse}.CachedInputTokens"/>
    /// is still populated when the provider surfaces it.</summary>
    Default,

    /// <summary>The mechanism adds <c>cache_control: { type: "ephemeral" }</c>
    /// to the system <see cref="ChatMessage.AdditionalProperties"/>.
    /// Anthropic interprets this as a 5-minute ephemeral cache marker;
    /// OpenAI ignores the hint (auto-caching is unchanged); other
    /// providers ignore the hint without error.</summary>
    Hinted
}
```

Two arms only. A third arm `Disabled` would be a lie — providers'
automatic caching can't be disabled from this abstraction layer.
Future arms (TTL-bound, per-block) are non-breaking additions.

### 2. `LlmCallDescriptor` gains `SystemPromptCache`

`WebReaper.AI/Llm/LlmCallDescriptor.cs`. New init-only field appended
(records are forward-compatible with appended `init` properties — no
positional-ctor break):

```csharp
/// <summary>Per-role caching policy for the system prompt (ADR-0065).
/// Default <see cref="CachePolicy.Default"/> — providers that auto-cache
/// still benefit; explicit-hint providers (Anthropic) do not. Set to
/// <see cref="CachePolicy.Hinted"/> to add the provider-specific
/// <c>cache_control</c> hint to the system message.</summary>
public CachePolicy SystemPromptCache { get; init; } = CachePolicy.Default;
```

The four built-in adapters do NOT set this field directly — they
inherit the default. `AiOptions` flows the user's chosen policy
through the `Effective*` merge helpers (piece 6).

### 3. `LlmCallResult` expands with split-usage fields

`WebReaper.AI/Llm/LlmCallResult.cs`. The record's positional shape
grows from 4 → 7 fields; `TotalTokens` stays for back-compat
(ADR-0051's `MaxBudgetTokens` reads it):

```csharp
public sealed record LlmCallResult<TResponse>(
    TResponse Value,
    long? InputTokens,
    long? OutputTokens,
    long? CachedInputTokens,
    long? TotalTokens,
    string RawResponse,
    int ParseRetries);
```

Field meanings:

- **`InputTokens`** — `ChatResponse.Usage?.InputTokenCount` (verified
  against M.E.AI 9.4-preview `UsageDetails`). Per Anthropic and OpenAI
  conventions this is *inclusive* of cached reads (the full prefix);
  per-provider normalisation is the adapter's job.
- **`OutputTokens`** — `ChatResponse.Usage?.OutputTokenCount`.
- **`CachedInputTokens`** — read from
  `ChatResponse.Usage?.AdditionalCounts` (verified — M.E.AI 9.4
  ships `UsageDetails.AdditionalCounts` as a typed dictionary of
  summable values, doc'd: *"all values set here are assumed to be
  summable"* — which explicitly endorses the retry-accumulator pattern).
  Adapter-specific key string (`cached_input_tokens` is the common
  convention; the OpenAI adapter and Anthropic community adapters
  vary — Implementation §4 ships a small key-name lookup). Subset of
  `InputTokens` — the portion that was a cache read. `null` when no
  recognised key matches.
- **`TotalTokens`** — kept for back-compat. Equals
  `InputTokens + OutputTokens` when both surfaced; else
  `Usage?.TotalTokenCount` directly; else `null`.

The mechanism is the only constructor of `LlmCallResult` — growing
the positional ctor is a source-level change to one file
(`LlmCall.cs`). Read-side consumers (the four adapters; future
consumer-authored adapters) consume named properties; the new fields
are opt-in additive at the read site.

### 4. Mechanism reads the split usage

`WebReaper.AI/Llm/LlmCall.cs`. `InvokeAsync` (and the retry path) gains:

```csharp
var usage = response.Usage;
var input  = usage?.InputTokenCount;
var output = usage?.OutputTokenCount;
var cached = TryReadCachedInputTokens(usage); // see piece 5
var total  = (input, output) switch
{
    (long i, long o) => (long?)(i + o),
    _                => usage?.TotalTokenCount
};

return new LlmCallResult<TResponse>(value, input, output, cached, total, raw, retries);
```

On retry, the two calls' fields sum via the same null-respecting
logic the existing `TotalTokens` accumulator already uses.

### 5. Mechanism encodes the cache hint

`WebReaper.AI/Llm/LlmCall.cs`. `CallAsync` (the internal per-call
method) gains:

```csharp
var systemMessage = new ChatMessage(ChatRole.System, _descriptor.SystemPrompt);
if (_descriptor.SystemPromptCache == CachePolicy.Hinted)
{
    systemMessage.AdditionalProperties ??= new AdditionalPropertiesDictionary();
    systemMessage.AdditionalProperties["cache_control"]
        = new Dictionary<string, object?> { ["type"] = "ephemeral" };
}
```

This is the Anthropic-standard encoding. M.E.AI 9.4-preview's
`ChatMessage.AdditionalProperties` (verified — type
`AdditionalPropertiesDictionary`, doc'd as *"any additional properties
associated with the message"*) is the documented metadata channel.
OpenAI / Gemini / local adapters either translate known keys to their
wire format or silently drop unknowns — `AdditionalProperties` is
metadata an adapter may or may not transmit. The descriptor-level
surface is stable; whether the `cache_control` key crosses the wire
depends on the consumer's chosen adapter (see Fork 10 + Implementation
Verification).

`TryReadCachedInputTokens(usage)` reads `usage.AdditionalCounts` for
the known keys (`cached_input_tokens`, `prompt_tokens_details.cached_tokens`,
…) — implementation ships a small lookup. The contract: `null` when
no recognised key matches.

### Note — M.E.AI's `UseDistributedCache` is a different mechanism

M.E.AI ships a `ChatClientBuilder.UseDistributedCache(IDistributedCache)`
middleware that caches *full responses* keyed by prompt hash — the
identical-prompt case never reaches the model. That is a *consumer-side*
cache and is orthogonal to this ADR's *provider-side* prompt caching
(partial-prefix cache the model itself maintains, transparently). The
two compose: `UseDistributedCache` can wrap an `IChatClient` that
itself benefits from provider-side caching on the cache-miss path.
This ADR addresses the provider-side mechanism — the larger win for
real-world crawls where prompts vary per-page but share a stable
prefix.

### 6. `AiOptions` flows a default

`WebReaper.AI/AiOptions.cs`. The record gains a new init field:

```csharp
public sealed record AiOptions(
    string? Model = null,
    float Temperature = 0.0f,
    int MaxResponseTokens = 4096,
    bool MarkdownPreClean = true,
    CachePolicy CachePolicy = CachePolicy.Hinted,   // ← new; default On.
    LlmExtractorOptions? Extractor = null,
    LlmActionResolverOptions? Resolver = null,
    LlmAgentBrainOptions? Brain = null,
    LlmExtractorOptions? Repairer = null,
    AiPolicyMode Policy = AiPolicyMode.Recommended);
```

Default `Hinted` — see Fork 1. Anthropic users benefit by default;
OpenAI users see no change (auto-cache continues); Gemini / local
users see the metadata ignored.

The per-role records (`LlmExtractorOptions`, `LlmActionResolverOptions`,
`LlmAgentBrainOptions`, and the existing `LlmExtractorOptions` reused
for the repairer) each grow:

```csharp
public CachePolicy? CachePolicy { get; init; } = null; // null = inherit from AiOptions
```

The `Effective*` merge helpers in `LlmRegistration` /
`LlmAgentRegistration` (ADR-0064) carry `perRole?.CachePolicy ?? global.CachePolicy`
into the descriptor.

### 7. Consumer surface — zero new methods

`.UseAi(client)` already exists. Caching ships through it:

```csharp
// On by default (the AI-native one-liner enables caching):
.UseAi(client)

// Off explicitly:
.UseAi(client, new AiOptions(CachePolicy: CachePolicy.Default))

// Per-role: cache the long-prefix extractor, not the short-prefix resolver:
.UseAi(client, new AiOptions(
    CachePolicy: CachePolicy.Hinted,
    Resolver: new LlmActionResolverOptions(CachePolicy: CachePolicy.Default)))
```

The à-la-carte `WithLlm*` methods pass `LlmExtractorOptions` / etc.
through unchanged — consumers who hand-wire (skipping `.UseAi`) set
the per-role `CachePolicy` directly.

## Considered options

### Fork 1 — Default policy

| Option | What | Verdict |
|---|---|---|
| (a) `Default` (no hint) | Safe default; opt-in to caching. | Rejected. Anthropic users pay 5–10× more by default; the AI-native ethos is "cheaper by default when safe." A consumer reading the API discovery surface should see caching enabled, not buried. |
| (b) `Hinted` (add cache hint) | Default-on. Anthropic users benefit; others no-op. | **Recommended.** Metadata is ignored by providers that don't read it; the one-call-only case (where the 1.25× cache-write premium is a small loss) is the edge, multi-page crawls are the dominant case. |
| (c) Auto-detect provider | Sniff `IChatClient` type / model id to decide. | Rejected. `IChatClient` is the abstraction — sniffing breaks the layering. The "hint is metadata, ignored if unknown" model is the right abstraction; the ignore-cost is zero. |

### Fork 2 — Enum vs. bool

| Option | What | Verdict |
|---|---|---|
| (a) Bool `EnableSystemPromptCache = true` | Single yes/no. | Rejected. Two arms today; future arms (per-role TTL — Anthropic offers 1 h beta, per-block cache for tools, explicit `Disabled` if a future provider lets us turn off auto-cache) want a closed type. Enum is forward-compatible at zero call-site cost. |
| (b) `CachePolicy` enum with `Default` / `Hinted` | Two arms; room for future widening. | **Recommended.** |
| (c) Three-arm with explicit `Disabled` | Third arm to "explicitly turn caching off." | Rejected. Cannot honestly suppress provider auto-caching from this layer — `Disabled` would be a lie about OpenAI. Adding the arm later is non-breaking when (if) the underlying API lets us actually disable. |

### Fork 3 — Where the hint is encoded

| Option | What | Verdict |
|---|---|---|
| (a) On the system `ChatMessage.AdditionalProperties` | Per-message metadata. | **Recommended.** Matches Anthropic's per-message-block model; the right level of granularity for v2 widening (cache other messages). |
| (b) On `ChatOptions.AdditionalProperties` | Per-call rather than per-message. | Rejected. Anthropic's `cache_control` is per-message-block; encoding at the request level loses granularity. The Anthropic adapter would have to re-attribute back to a message; ambiguity for multi-cacheable-block prompts. |
| (c) On the content block (`TextContent` inside the message) | One level deeper. | Considered. Some M.E.AI adapter versions expect the hint on the content rather than the message. Implementation verifies which level the current adapter expects — the descriptor-level surface is the same. |

### Fork 4 — Result shape: expand vs. nested

| Option | What | Verdict |
|---|---|---|
| (a) Expand positional record from 4 → 7 fields | Add `InputTokens` / `OutputTokens` / `CachedInputTokens` directly. | **Recommended.** Simpler; the mechanism is the only constructor, so the positional-ctor change is one file. Read-side consumers use named properties. |
| (b) Nested `LlmUsage` record | `LlmCallResult.Usage` is a sub-record. | Rejected. Adds one indirection for one record's data; four flat `long?` fields don't warrant a wrapper. |
| (c) Drop `TotalTokens`; compute from sum at read sites | Don't carry redundant data. | Rejected. ADR-0051's `MaxBudgetTokens` reads `TotalTokens` directly — back-compat. Keep both; document `TotalTokens` as derived. |

### Fork 5 — Per-role default

| Option | What | Verdict |
|---|---|---|
| (a) Global default in `AiOptions`; per-role nullable override | One knob; per-role wins when non-null. | **Recommended.** Matches ADR-0064's per-field-nullable pattern. |
| (b) Per-role only (no global) | Each adapter's options has its own field. | Rejected. Repeats the choice four times for the common case. |
| (c) Global only (no per-role) | One knob for the run. | Rejected. A consumer wanting caching on the extractor (long, stable prompt) but not the resolver (short, varying few-shot rotates) can't express it. |

### Fork 6 — Descriptor-owned hint vs. adapter-owned hint

| Option | What | Verdict |
|---|---|---|
| (a) Descriptor field; mechanism encodes | One implementation in `LlmCall`. | **Recommended.** Consistent with ADR-0059 — the mechanism owns provider-specific encoding (just as it owns code-fence stripping). |
| (b) Each adapter sets `AdditionalProperties` itself in its descriptor-build code | More flexibility per adapter. | Rejected. Replicates encoding four times — the same duplication ADR-0059 eliminated. The hint format is a mechanism concern, not a role concern. |

### Fork 7 — Cache key / identity management

| Option | What | Verdict |
|---|---|---|
| (a) Mechanism does not manage cache identity | Hint is metadata; provider hashes the prompt and identifies its own cache. | **Recommended.** Matches Anthropic's and OpenAI's model — both compute cache keys from prompt content. The mechanism's job ends at "I hinted." |
| (b) Mechanism manages an explicit cache id | Per-Crawl cache id passed through to the provider. | Rejected. Anthropic / OpenAI don't accept caller-side cache ids. (Gemini does, but `cachedContent` is the deferred surface.) Speculative generality. |

### Fork 8 — User-message prefix caching (v2 deferral)

| Option | What | Verdict |
|---|---|---|
| (a) Cache the user-message prefix too | The extractor's schema JSON is invariant per crawl; cacheable. | Rejected (v2). v1 caches the descriptor-invariant system prompt only — the highest-value fixed prefix. v2 may add a `UserPromptCachePrefix` field on the descriptor and have the mechanism split the user message at that prefix. |

### Fork 9 — Tool-registry caching (v2 deferral)

| Option | What | Verdict |
|---|---|---|
| (a) Cache the tool definitions array | Anthropic supports `cache_control` on tools; brain has 9, resolver has 6 (ADR-0060). | Rejected (v2). Out of scope for v1; the system-prompt cache captures the highest-value bytes. v2 may extend the cached span to include the tool array (Anthropic's multi-block caching). |

### Fork 10 — Adapter ignore-or-error risk

| Option | What | Verdict |
|---|---|---|
| (a) Trust adapters to ignore unknown `AdditionalProperties` keys | M.E.AI documents `AdditionalProperties` as the message-metadata channel (verified 9.4-preview); adapters serialise known keys and ignore unknowns. | **Recommended.** Pre-merge spot-check: confirm `cache_control` is either translated (Anthropic community adapters) or no-op'd (the first-party `Microsoft.Extensions.AI.OpenAI`, Ollama). If a chosen adapter throws on unknown keys, the mechanism gates on adapter id — minor revision, no descriptor change. |
| (b) Namespace the key (`webreaper.cache_control`) | Adapters must opt in to read. | Rejected. Defeats "add hint, provider reads"; would require WebReaper-aware adapters. |
| (c) Don't write to `AdditionalProperties`; `CachePolicy` is intent-only | Document intent; no wire change. | Rejected. The point is to *actually hint the provider*. Recording intent is for ADR-0066's telemetry, not for the wire. |

## Consequences

- **Anthropic users get 5–10× cheaper system prompts by default.**
  `.UseAi(client)` with the default policy = `Hinted` adds the
  `cache_control` hint on every `LlmCall<T>` invocation. Anthropic's
  5-minute TTL covers most multi-page crawl bursts.
- **OpenAI users see no behaviour change.** OpenAI's auto-cache
  continues; the `cache_control` metadata is ignored by the adapter.
  `CachedInputTokens` is still populated when the provider surfaces
  cached counts.
- **Gemini and local-model users see no error.** The unknown
  `cache_control` property is ignored by adapters that don't read it.
- **`LlmCallResult` surfaces cached / uncached split tokens.** ADR-0066
  turns this into engine-level cost telemetry (input vs cached-input
  vs output spend).
- **Single-call edge case — caching loses ~25%.** A consumer running a
  single-page scrape pays the cache-write premium with no second hit
  to amortise. The break-even is ~2 calls within TTL. WebReaper's
  dominant use is multi-page crawls; the per-role override (`AiOptions
  with { Resolver: new LlmActionResolverOptions(CachePolicy: Default) }`)
  is the escape for single-shot consumers.
- **`AgentEngineOptions.MaxBudgetTokens` continues to read
  `TotalTokens`.** Currently overcounts cached reads at full price;
  ADR-0066 introduces a weighted-budget variant if the user wants it.
- **No core changes.** Entirely in `WebReaper.AI`. No `IContentExtractor`
  / `IAgentBrain` / `IActionResolver` surface touched.
- **Per-role override available.** A consumer wanting caching on the
  extractor but not the brain sets `AiOptions(Brain: new
  LlmAgentBrainOptions(CachePolicy: CachePolicy.Default))`.
- **CONTEXT.md** gains **Cache policy** term in the AI-native section;
  relationship line linking policy → mechanism → result.
- **CLAUDE.md** gets a one-line gotcha — `CachePolicy.Hinted` is the
  default in `AiOptions`; Anthropic users benefit by default, OpenAI
  users see no change (auto-cache continues), Gemini / local-model
  users see the hint ignored without error.

## Bounded scope (v1)

- **No user-message prefix caching.** Fork 8 — v2 deferral.
- **No tool-registry caching.** Fork 9 — v2 deferral.
- **No Gemini `cachedContent`.** Different surface — separate ADR
  fodder later.
- **No explicit cache-id management.** Fork 7 — providers own
  identity.
- **No mechanism-level "disable provider auto-cache".** OpenAI's
  auto-cache cannot be suppressed from this abstraction.

## Implementation (slice, when accepted)

**Satellite — one new file, three edited:**

1. **`WebReaper.AI/Llm/CachePolicy.cs`** — new public enum.
2. **`WebReaper.AI/Llm/LlmCallDescriptor.cs`** — append
   `SystemPromptCache` init field; default `CachePolicy.Default`.
3. **`WebReaper.AI/Llm/LlmCallResult.cs`** — expand record to 7-arity
   positional ctor (`InputTokens`, `OutputTokens`, `CachedInputTokens`
   inserted before existing `TotalTokens`).
4. **`WebReaper.AI/Llm/LlmCall.cs`** —
   - `CallAsync`: write the cache hint on the system message when
     `_descriptor.SystemPromptCache == CachePolicy.Hinted`.
   - `InvokeAsync`: read `InputTokenCount` / `OutputTokenCount` /
     `AdditionalCounts[<cached-key>]` via a new private
     `TryReadCachedInputTokens(UsageDetails?)` helper; accumulate the
     four-field tuple on retry.
   - New unit-test-only `internal const string CachedInputTokensKey` for
     the M.E.AI key (verified during implementation).

5. **`WebReaper.AI/AiOptions.cs`** — append
   `CachePolicy CachePolicy = CachePolicy.Hinted` to the record.
6. **`WebReaper.AI/LlmExtractorOptions.cs`** +
   **`LlmActionResolverOptions.cs`** + **`LlmAgentBrainOptions.cs`** —
   each gains a `CachePolicy? CachePolicy` init field (null = inherit).
7. **`WebReaper.AI/LlmRegistration.cs`** +
   **`LlmAgentRegistration.cs`** — `Effective*` merge helpers carry
   `perRole?.CachePolicy ?? global.CachePolicy` into the descriptor
   build.

**Verification step (during implementation):**
M.E.AI 9.4-preview type surfaces are already verified — `ChatMessage.AdditionalProperties`
(type `AdditionalPropertiesDictionary`, object-valued), `UsageDetails.{InputTokenCount,
OutputTokenCount, TotalTokenCount, AdditionalCounts}` all confirmed via the abstractions XML
docs. The remaining wire-level verification is adapter-specific:
- Stub `IChatClient` capturing `ChatMessage.AdditionalProperties` — assert `cache_control`
  present when `Hinted`, absent when `Default` (unit, fast).
- Stub `IChatClient` returning `UsageDetails(AdditionalCounts: { "cached_input_tokens": 900 })`
  — assert `LlmCallResult.CachedInputTokens == 900`; same with key absent → null (unit, fast).
- Spot-check against the first-party `Microsoft.Extensions.AI.OpenAI` adapter — confirm
  it doesn't throw on the unknown `cache_control` key (the hint is no-op for OpenAI but must
  not break the call).
- Anthropic wire-format confirmation deferred to consumer integration — the project doesn't
  bundle an Anthropic adapter (community-maintained `Anthropic.SDK.MicrosoftExtensions.AI`
  or equivalent); CLAUDE.md gotcha notes the adapter-dependence.

**Tests — `WebReaper.Tests/WebReaper.AI.Tests/`:**

8. **`CachePolicyTests.cs`** (new) — pin every encoding:
   - `Default` policy: no `cache_control` on system-message
     `AdditionalProperties`.
   - `Hinted` policy: `cache_control` present with
     `{ type: "ephemeral" }`.
   - `Hinted` policy on retry: hint present on both first and retry
     calls.
   - `Hinted` policy on tool-call mode (descriptor with `Tools`): hint
     still present on the system message — tool-call mode doesn't
     change the encoding location.
   - Cached-tokens parse: stub returns
     `UsageDetails(InputTokenCount: 1000, OutputTokenCount: 50,
     AdditionalCounts: { "cached_input_tokens": 900 })` →
     `LlmCallResult(InputTokens: 1000, OutputTokens: 50,
     CachedInputTokens: 900, TotalTokens: 1050)`.
   - Cached-tokens absent: `CachedInputTokens` is `null`; everything
     else populated; `TotalTokens` falls back to `Usage.TotalTokenCount`
     when `InputTokenCount` / `OutputTokenCount` are themselves null.
9. **`LlmContentExtractorCachingTests.cs`** (new) — assert the
    extractor's descriptor inherits `AiOptions.CachePolicy` through
    `.UseAi(client)` and the cache hint is present on the outbound
    system message.
10. **`AiOptionsCachingTests.cs`** (new) — per-role overrides:
    - Global `Hinted`, per-role `Default` → that role's descriptor
      gets `Default`.
    - Global `Default`, per-role `Hinted` → that role's descriptor
      gets `Hinted`.
    - Global `Hinted`, per-role null → that role's descriptor gets
      `Hinted`.

**Docs:**

11. **CONTEXT.md** — AI-native section gains **Cache policy** term;
    relationship line: *policy → mechanism (encodes hint) → result
    (carries cached/uncached split)*.
12. **CLAUDE.md** — gotcha line under the AI-native bullets:
    `CachePolicy.Hinted` is the default in `AiOptions`; Anthropic
    users benefit, OpenAI users see no change, Gemini / local-model
    users see the hint ignored without error. Single-page scrapes pay
    a ~25% cache-write premium with no second hit to amortise —
    override per-role to `Default` for one-shot consumers.

### Guardrails (when implemented)

- `dotnet build WebReaper.sln` — 0 errors.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — all pass (no
  core surface touched).
- `dotnet test WebReaper.Tests/WebReaper.AI.Tests` — all existing tests
  pass; new caching tests pass.
- `WebReaper.AotSmokeTest` — unchanged (the satellite is not
  AOT-required by ADR-0009).

## References

- ADR-0009 — registration seam + satellite pattern; caching lives in
  `WebReaper.AI`, not core.
- ADR-0044 — LLM extractor; the highest-system-prompt-token adapter;
  primary beneficiary.
- ADR-0051 — agent driver; `MaxBudgetTokens` reads `TotalTokens` —
  currently overcounts cached reads. ADR-0066 surfaces the
  cached/uncached split for honest accounting.
- ADR-0059 — `LlmCall<TResponse>` mechanism module; this ADR extends
  the descriptor + mechanism + result.
- ADR-0060 — tool-calling brain + resolver; the brain's system
  prompt + tool registry is the biggest cache opportunity (v2
  extension: cache the tool array too).
- ADR-0064 — `.UseAi(...)` policy; this ADR adds the global
  `CachePolicy` field that flows down the `Effective*` merge helpers.
- ADR-0066 — engine cost telemetry; the consumer of this ADR's
  per-call `InputTokens` / `OutputTokens` / `CachedInputTokens`
  fields.
