# `PageAction.SemanticAct(intent)` — semantic page actions; LLM resolves at runtime, cache demotes to deterministic

## Status

**Accepted — implemented** (2026-05-24). Sibling pattern to ADR-0047
(self-healing extractor) applied to the *action* surface instead of the
*extraction* surface. Lands on the closed sum from ADR-0035 and the
satellite-resolver pattern from ADR-0044/0047. Implementation slice
shipped in the 10.0.0 wave: see the file ledger in §Implementation
(all items landed). Two cache-key forks deferred to v2 per
"Considered options" (a) and (e).

## Context

ADR-0035 made `PageAction` a closed sum of six typed arms — every existing
arm is **selector-based**: `Click(selector)`, `WaitForSelector(selector,
timeoutMs)`, `EvaluateExpression(jsExpression)`. A renamed class on the
target page breaks the action the same way it once broke extraction.

ADR-0047 shipped the answer for extraction: *LLM proposes selectors, the
deterministic fold validates, the patched schema is cached, the
deterministic path stays the hot path.* The action surface has the same
fragility class and no analogous answer. A renamed login-button class
today means the consumer:

1. Reads the failed crawl.
2. Inspects the new page HTML.
3. Edits the `Click(selector)` literal.
4. Redeploys.

This is the *exact* loop ADR-0047 closed for extraction. The 2026-05-24
Browserbase/Stagehand research surfaces the same pattern at a different
project: Stagehand's `observe("click sign in") → act(cached_action)` is
"LLM-proposes-once, deterministic-thereafter" with cache demotion — the
firecrawl-research-digest insight #4 again, applied to actions.

The plan §2.2 framed self-heal as a content-extraction mechanism. This
ADR extends it: **self-heal is a *pattern*, not a *feature* — the seat the
extraction wave shipped on (ADR-0046 router, ADR-0047 self-heal, ADR-0048
change-track) generalises to actions.** The page-action surface is the
next dock.

### What "the action cache" really is

Stagehand caches the **concrete CDP/selector action** the LLM resolved an
intent to. The cache lifetime is the workflow run; re-running the
intent with a stable page reuses the cached resolution.

For WebReaper: the cache stores a **concrete `PageAction` arm per intent
string**, in-memory, per crawl. On the first page that invokes
`SemanticAct("click sign in")`:

1. Cache miss. Resolver examines the page HTML, returns
   `Click(".header-nav button.signin-btn")`.
2. Cache `"click sign in"` → that arm.
3. Puppeteer dispatches the `Click` arm normally.
4. Subsequent pages with the same intent hit the cache — no LLM call,
   the resolved arm is dispatched directly.

If the cached arm later fails (selector now missing), the wrapper re-
invokes the resolver — same shape as ADR-0047's "validate, then re-
repair on subsequent failure."

### Cache key — the design call

`IActionResolver.ResolveAsync(intent, html, ...)` carries the intent
string. The key options:

| Key | Captures | Risk |
|---|---|---|
| Intent string | "Same intent, anywhere" | Two pages with different DOMs but the same intent reuse one resolution — wrong if the resolution was DOM-specific. |
| `(intent, host)` | "Same intent, same host" | The common case — one site, one resolution per intent. |
| `(intent, schema-of-html)` | "Same intent, same DOM shape" | Most defensive; needs cheap DOM-shape hashing. |

v1 ships **intent string alone**, in-memory, per crawl — the cleanest. A
single Crawl is conventionally one host (the start URL); the cache resets
between Crawls. Multi-host crawls (`AllowOffsite=true` per ADR-0042
`MapOptions`) get the same cache; if that causes mis-resolution, the
deferred v2 enhancement is per-host keying. Same discipline as ADR-0047
(reference-identity v1, hash + URL host as deferred v2).

### The resolver protocol

`IActionResolver` is a one-method interface. Given the intent string and
the current page's rendered HTML, it returns either a concrete `PageAction`
arm or `null` (couldn't resolve).

The default resolver in `WebReaper.AI` calls the LLM with:

```
System: You are resolving a user's intent to a concrete browser action.
Given an HTML page and an intent like "click sign in", return a JSON
object with one of the supported action shapes:

  { "kind": "click",       "selector": "<css>" }
  { "kind": "wait",        "ms":       <int>   }
  { "kind": "waitFor",     "selector": "<css>", "timeoutMs": <int> }
  { "kind": "evaluate",    "expression": "<js>" }

Pick the simplest action that satisfies the intent. Prefer a CSS
selector specific enough not to collide. Return JSON only.

User:
Intent: <intent>
Page (HTML, truncated to <N>k tokens): <html>
```

The resolver parses the JSON and constructs the matching `PageAction`
arm. Anything outside the supported shapes is `null` (unsupported).

### Validation discipline

The self-healing extractor cached after the fold *validated*. Actions
have no value to validate — but they have a verifiable *effect*: the
selector either exists on the page (a `Click` succeeds) or it does not (a
`Click` throws / no-ops / times out).

v1's validation is **the dispatched action's success/failure**. A
resolved-and-dispatched arm that throws (selector missing,
WaitForSelector times out) is *not* cached — the resolver is invoked
again on the next page. A resolved arm that succeeds *is* cached. Same
LLM-proposes/local-verifies discipline as ADR-0047, with the verification
being action dispatch rather than fold output.

A deferred v2: a `verify` callback per intent (e.g. *"click sign in" is
satisfied iff a `.user-menu` element appears within 2s of dispatch*).
Out of scope for v1.

## Decision

Four pieces — one new arm, one new seam, one wrapper, one satellite
adapter — landing on the seats ADR-0035 + ADR-0009 + ADR-0044 already
expose.

### 1. `PageAction.SemanticAct(string Intent)` — the new arm

[WebReaper/Domain/PageActions/PageAction.cs](../../WebReaper/Domain/PageActions/PageAction.cs).
A new arm in the existing closed sum:

```csharp
public abstract record PageAction
{
    private PageAction() { }
    // existing: Click, Wait, ScrollToEnd, EvaluateExpression,
    // WaitForSelector, WaitForNetworkIdle ...

    /// <summary>Resolve a natural-language intent to a concrete action at
    /// runtime via the registered <see cref="IActionResolver"/>; cache the
    /// resolution per crawl. ADR-0050.</summary>
    public sealed record SemanticAct(string Intent) : PageAction;
}
```

`PageActionBuilder` gets one new method:

```csharp
public PageActionBuilder SemanticAct(string intent)
```

### 2. `IActionResolver` — the resolution seam

[WebReaper/Core/Actions/Abstract/IActionResolver.cs](../../WebReaper/Core/Actions/Abstract/IActionResolver.cs)
(new directory). Public interface, one method:

```csharp
public interface IActionResolver
{
    Task<PageAction?> ResolveAsync(
        string intent,
        string pageHtml,
        CancellationToken cancellationToken = default);
}
```

A `null` return means "I tried, couldn't resolve" — the transport throws
`SemanticActResolutionException` (a new typed exception so consumers
catch the failure mode, not a bare `NullReferenceException`).

The default registration when none is supplied is **`NullActionResolver`**,
which returns `null` and logs a "no `IActionResolver` registered;
`SemanticAct` is unusable until `WithActionResolver(...)` is called"
warning on engine construction *only if* the config's selector chain
contains a `SemanticAct`. No silent failure.

### 3. The Puppeteer satellite dispatches `SemanticAct` via the resolver

The dispatch switch in
[WebReaper.Puppeteer/PageActionExecutor.cs](../../WebReaper.Puppeteer)
gains one case:

```csharp
PageAction.SemanticAct(var intent) => await DispatchSemantic(intent, page),
```

Where `DispatchSemantic`:

1. Read the page's HTML (`page.GetContentAsync()`).
2. Look up `intent` in the per-crawl cache. If present, dispatch the
   cached arm. If it throws (selector missing, timeout), invalidate and
   fall through.
3. Cache miss: call `resolver.ResolveAsync(intent, html, ct)`. If
   `null`, throw `SemanticActResolutionException`.
4. Dispatch the resolved arm. If it succeeds, cache it. If it throws,
   surface the exception — the LLM proposed something the page
   couldn't honour; honest failure beats silent retry.

The cache lives in the `PageActionExecutor` per Spider — the same
lifetime as the crawl. (Spider is constructed per crawl by ADR-0034.)

### 4. `LlmActionResolver` — the `WebReaper.AI` satellite implementation

[WebReaper.AI/LlmActionResolver.cs](../../WebReaper.AI). Implements
`IActionResolver` via `IChatClient` — same Microsoft.Extensions.AI binding
as ADR-0044. Constructor mirrors `LlmContentExtractor`:

```csharp
public LlmActionResolver(IChatClient chatClient, LlmActionResolverOptions? options = null)
```

`LlmActionResolverOptions` is a small record: `MaxHtmlTokens` (default
8192, page-HTML pre-trim before the prompt), `Temperature` (default `0`),
`MaxResponseTokens` (default `512`). No new dependencies.

### 5. `ScraperEngineBuilder.WithActionResolver` — the builder sugar

```csharp
public ScraperEngineBuilder WithActionResolver(IActionResolver resolver)
```

The `WebReaper.AI` satellite ships
`WithLlmActionResolver(IChatClient chatClient, LlmActionResolverOptions? options = null)`
that builds the `LlmActionResolver` and calls `WithActionResolver`.

### Bounded scope

- **In-memory cache only** in v1. A persistent cache (Redis / File) is a
  future satellite ADR; the seam already supports it (resolver-wrapping
  decorator).
- **Single-host crawls assumed.** Cache key is the intent string. Multi-
  host extension (`(intent, host)`) is deferred — see "considered (a)".
- **Resolves to existing arms only** — the LLM cannot invent a new arm
  shape. The closed sum stays closed (ADR-0035's whole point).
- **No mid-page retry** — a failed resolved arm surfaces as an exception.
  Retrying with a different LLM proposal would loop on flaky selectors;
  the retry policy (ADR-0026) at the Spider level catches the whole job
  if appropriate.
- **No streaming / no token-budget governor.** Same v2 deferrals as
  ADR-0044 — wait for a real caller to prove the shape.

## Considered options

### (a) Per-host cache keying — rejected (deferred)

`(intent, host)` is more correct on multi-host crawls but needs the
host in the resolver-call signature *or* the cache-wrapper signature.
Single-host is the common case; multi-host is the `AllowOffsite=true`
edge. Defer until a real caller proves the shape.

### (b) Make `SemanticAct` resolve in core, not in the Puppeteer satellite — rejected

The page HTML lives in the satellite's `Page` object. Resolving in core
would require the core to know about the satellite's page type — re-
introducing the ADR-0009 quarantine break that the registration seam
exists to prevent. Core owns the seam (`IActionResolver`); the satellite
owns the invocation.

### (c) Add the resolver to `WebReaper.AI` as a separate dimension (not on the closed sum) — rejected

Stagehand-style: `await page.SemanticActAsync("click sign in")` as a
satellite extension on the Page, bypassing PageAction entirely. Cleaner
on the satellite side, but **the closed sum exists so the *config* is
serialisable and the *transport* is the only dispatcher** (ADR-0035).
A satellite-only Page extension would mean SemanticAct can't appear in a
durable `ScraperConfig` (ADR-0034) — it'd be code-only. The arm makes
SemanticAct equal to every other action: serialisable, replayable,
inspectable.

### (d) Cache the resolution per *page*, not per *crawl* — rejected

Per-page caching means re-resolving every page — defeats the cost-
amortisation. Per-crawl caching is the *ADR-0047 pattern* and the *
Stagehand pattern* — first page pays the LLM, subsequent pages are free.

### (e) Allow the LLM to return *multiple* candidate actions, transport picks one — rejected (deferred)

Stagehand's `observe` returns *candidate* actions; `act` picks one.
v1's "resolver returns one arm" is simpler. Multi-candidate adds the
candidate-selection question (pick first? validate each? expose to the
user?). Defer until a real caller proves the shape.

### (f) Add an `IActionVerifier` seam for the "did the action have its intended effect" check — rejected (deferred)

The dispatch-throws-or-succeeds verification is honest in v1. A semantic
verifier ("after `click sign in`, a `.user-menu` element should appear")
is a real future enhancement but adds a second seam and another LLM call
per intent — too much for v1. Lives in v2 when a real caller surfaces.

## Consequences

- **The self-heal pattern generalises across the extraction/action
  boundary.** ADR-0046, ADR-0047, ADR-0048 sat on the extraction seam;
  ADR-0050 sits on the action seam. The repositioning plan's
  "LLM-as-proposer, deterministic-as-decider" stops being one feature
  and becomes a *project-level* pattern.
- **The closed sum stays closed.** Adding `SemanticAct` is the
  ADR-0035-blessed extension shape — one more nested record on the same
  abstract base. The exhaustiveness analyzer keeps catching missing
  switch arms in `PageActionExecutor`.
- **`PageAction` becomes consumable from natural-language CLI / Skill
  flows.** A future CLI sub-command `webreaper crawl --click "sign in"`
  becomes one-line; the agent skill (ADR-0043) gains a higher-leverage
  primitive.
- **The deterministic path is the hot path.** First page pays the LLM
  resolution; every subsequent page in the crawl dispatches the cached
  concrete arm. The funnel's cost-curve claim (`LLM-quarantine`,
  REPOSITIONING-PLAN §2.4) extends from extraction to actions.
- **No new core deps.** `IActionResolver` is a one-method interface;
  `NullActionResolver` is one class; `SemanticAct` is one record. The
  AI dependency stays quarantined in `WebReaper.AI` per ADR-0009.
- **CONTEXT.md** gains an **Action resolver** term + relationship line.

## Implementation

Shipped. The slice landed exactly as designed, with three notable
refinements recorded inline:

1. **`WebReaper/Domain/PageActions/PageAction.cs`** —
   `SemanticAct(string Intent)` added as the seventh sealed-record arm;
   the doc updated "six arms" → "seven arms".
2. **`WebReaper/Builders/PageActionBuilder.cs`** — `.SemanticAct(intent)`
   with `ArgumentException.ThrowIfNullOrWhiteSpace`.
3. **`WebReaper/Core/Actions/Abstract/IActionResolver.cs`** — new
   public seam (one method).
4. **`WebReaper/Core/Actions/Concrete/NullActionResolver.cs`** —
   `internal sealed`, singleton `Instance` (stateless), returns `null`.
5. **`WebReaper/Core/Actions/Concrete/SemanticActResolutionException.cs`**
   — the typed exception the transport throws (two ctors: bare intent,
   intent + inner). Public.
6. **`WebReaper/Core/Actions/Concrete/SemanticActCoordinator.cs`** —
   *refinement.* The cache lifecycle + resolve-then-dispatch sequencing
   lives in core as a *public* class instead of being buried in the
   Puppeteer transport. This makes the asymmetric-retry contract
   unit-testable from core without `IPage` or Chromium (the
   `SemanticActDispatchTests` exercise it directly with stub callbacks).
   The transport instantiates one per Spider and delegates each
   `SemanticAct` case to `DispatchAsync`, supplying two `IPage`-bound
   callbacks (`getHtmlAsync` for the cache-miss HTML read; `dispatch`
   for the per-arm dispatcher). Per ADR-0023's deletion test, the
   coordinator passes Tier-1 (named by a documented consumer — the
   Puppeteer satellite — and would be inherited / re-used by any other
   transport satellite).
7. **`WebReaper/Serialization/Converters/PageActionJsonConverter.cs`**
   — the `"semanticAct"` codec entry with the `"intent"` field. The
   resolved arm is deliberately *not* persisted — see the inline comment
   in the converter (would freeze the LLM's selector across crawls,
   defeating the re-resolve-on-cache-miss recovery path).
8. **`WebReaper/Builders/SpiderBuilder.cs`** — `WithActionResolver`
   setter, `IActionResolver` field with `NullActionResolver.Instance`
   default, and the widened factory. *Refinement:*
   `WithLoadTransport`'s factory delegate widens from 3 to 4 arguments
   (the 4th is `IActionResolver`). This is the design's one breaking
   edge — the public registration seam's contract changes. The
   `WebReaper.Puppeteer` satellite is updated in lockstep; called out
   in the 10.0.0 CHANGELOG.
9. **`WebReaper/Builders/DistributedSpiderBuilder.cs`** — the
   distributed-worker reduced shell gained the same `WithActionResolver`
   + widened `WithLoadTransport`.
10. **`WebReaper/Builders/ScraperEngineBuilder.cs`** —
    `WithActionResolver` pass-through; `BuildAsync` runs the
    `WarnIfSemanticActWithoutResolver` check (scans
    `ScraperConfig.PageActions` + every `LinkPathSelector.PageActions`)
    and logs a Warning if any `SemanticAct` is present with the default
    `NullActionResolver` still registered.
11. **`WebReaper.Puppeteer/PuppeteerPageLoaderBuilderExtensions.cs`** —
    `WithPuppeteerPageLoader()` passes the resolver through.
12. **`WebReaper.Puppeteer/BrowserPageLoadTransport.cs`** — the
    dispatch case delegates to a per-transport `SemanticActCoordinator`;
    `PerformAsync` is now instance + carries the `CancellationToken`
    (`Task.Delay` honours it); the closed-sum `default` throw still
    names a future unhandled arm actionably.
13. **`WebReaper.AI/LlmActionResolver.cs`** — the satellite resolver.
    Prompt whitelists four shapes (`click` / `waitFor` / `wait` /
    `evaluate`) — *not* `semanticAct`. JSON parse → `ParseArm` (the
    closed whitelist); unknown shape → `null` (the transport surfaces
    the typed exception). Code-fence stripping mirrors
    `LlmContentExtractor`. *Refinement:* token-budget options use a
    character cap (`MaxHtmlChars`, default 32_000), not a token cap —
    keeps the satellite zero-dependency beyond
    `Microsoft.Extensions.AI.Abstractions`.
14. **`WebReaper.AI/LlmActionResolverOptions.cs`** — `record` with
    `Model`, `Temperature` (default 0), `MaxResponseTokens` (default
    512), `MaxHtmlChars` (default 32_000), `SystemPrompt`.
15. **`WebReaper.AI/LlmActionResolverRegistration.cs`** —
    `WithLlmActionResolver` extension method, mirrors
    `WithLlmExtractor`.
16. **`WebReaper.Tests/WebReaper.UnitTests/SemanticActDispatchTests.cs`**
    — 18 tests pinning every ADR-named guarantee on the coordinator
    (cache hit, cache miss, cached-arm-failure invalidates,
    resolver-returns-null throws, resolver-throws wraps,
    resolver-returns-SemanticAct surfaces, dispatch-failure doesn't
    cache, cancellation propagates, distinct intents distinct cache
    entries), the codec round-trip via `Job`, the builder, and the
    `BuildAsync` warning forks (warning fires / doesn't fire when no
    SemanticAct / doesn't fire when a resolver is registered).
17. **`WebReaper.Tests/WebReaper.AI.Tests/LlmActionResolverTests.cs`** —
    20 tests pinning every arm-shape resolution, the unknown-kind null
    contract, the never-returns-SemanticAct discipline, the code-fence
    stripping, the truncation, and the `ChatOptions` flow-through.
18. **CONTEXT.md** — new "Semantic action / Action resolver" term in
    the AI-native section + a new Relationships line linking the cache
    lifecycle to the proposer-validator pattern (ADR-0046, ADR-0047).
19. **CLAUDE.md** — the AI-native architecture paragraph extended from
    "ADR-0040..0049" to "ADR-0040..0050" with the semantic-actions
    bullet; two new gotchas (the dispatch-throws-without-a-resolver
    contract and the `WithLoadTransport` factory-signature breaking
    edge).
20. **CHANGELOG.md** — 10.0.0 entry top line updated to "AI-native
    funnel + semantic actions"; "24 ADRs" → "25 ADRs"; new bullet under
    "AI-native wave".

### Guardrails (verified at slice end)

- `dotnet build WebReaper.sln` — 0 errors, 23 pre-existing warnings.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — **256/256** pass
  (was 238; +18 from `SemanticActDispatchTests`).
- `dotnet test WebReaper.Tests/WebReaper.AI.Tests` — **42/42** pass
  (was 22; +20 from `LlmActionResolverTests`).
- `dotnet test WebReaper.Tests/WebReaper.Puppeteer.Tests` — 4/4 pass
  (the existing wire-up smoke is unchanged; the dispatch logic moved
  to core where it's testable without Chromium).
- `WebReaper.AotSmokeTest` — unchanged graph; core gained one
  `SemanticActCoordinator` + the `ConcurrentDictionary` it owns, no
  AOT-hostile reflection.

## References

- ADR-0009 — registration seam + satellite adapter pattern; the resolver
  registration is one more entry.
- ADR-0035 — `PageAction` closed sum; the dock `SemanticAct` lands on.
- ADR-0044 — `WebReaper.AI` LLM-extractor satellite; the project the
  resolver ships in, the same `IChatClient` binding.
- ADR-0046 — `ExtractionRouter`; sibling mechanism, applies the
  proposer-validator pattern to extraction.
- ADR-0047 — `SelfHealingContentExtractor`; closest design parallel
  (resolver-with-cache, same cache-key discipline, same
  in-memory-v1-persistence-v2 bounded scope).
- ADR-0026 — retry policy seam; the Spider-level retry catches a
  whole-job failure (a `SemanticActResolutionException` propagates
  through it).
- REPOSITIONING-PLAN §2.2 — the self-heal pattern this generalises.
- *2026-05-24 Browserbase/Stagehand research notes* — the
  observe-then-act + self-heal model the cache-demotion shape is taken
  from.
