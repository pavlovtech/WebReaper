# `LlmCall<TResponse>` — the deep LLM-invocation module the four AI adapters share

## Status

**Proposed** (2026-05-24). First ADR of the post-AI-native-wave
deepening campaign. The four LLM adapters shipped in 10.0.0
(`LlmContentExtractor` — ADR-0044, `LlmSelectorRepairer` — ADR-0047,
`LlmActionResolver` — ADR-0050, `LlmAgentBrain` — ADR-0051) each
reimplement the same call-the-model-and-parse mechanism. Extracts that
mechanism into one deep module under `WebReaper.AI/Llm/` so the
adapters shrink to "what's my descriptor." Bedrock for ADR-0060's
tool-calling pivot. Folds into a v10.x point release; satellite-only —
no core surface touched.

## Context

Four LLM adapters now live in the `WebReaper.AI` satellite. Reading
them side-by-side, the *shape* is identical:

| Step | LlmContentExtractor | LlmSelectorRepairer | LlmActionResolver | LlmAgentBrain |
|---|---|---|---|---|
| 1. System prompt | `DefaultSystemPrompt` const | `DefaultSystemPrompt` const | `DefaultSystemPrompt` const | `DefaultSystemPrompt` const |
| 2. User message | schema + cleaned content | original selectors + failed result + page | intent + truncated HTML | state-as-string |
| 3. `ChatOptions` | `Model`, `Temperature`, `MaxOutputTokens`, `ResponseFormat = Json` | same | same | same |
| 4. `GetResponseAsync(...)` | yes | yes | yes | yes |
| 5. `response.Text ?? ""` | yes | yes | yes | yes |
| 6. `StripJsonFences(text)` | yes | yes | yes | yes |
| 7. `JsonNode.Parse(text)` + cast | yes | yes | yes | yes |
| 8. Domain-type construction | `(JsonObject)parsed` | walk schema swap selectors | `ParseArm(obj)` | `ParseDecision(obj)` |
| 9. Usage capture | no | no | no | no |
| 10. Parse-failure handling | throw | return null | return null | return `Stop(reason)` |

**Steps 1-7 are pure mechanism** — prompt-marshalling, transport, text-
extraction, code-fence stripping, JSON parse. The mechanism is
*deduplicated by copy-paste*: `StripJsonFences` appears four times,
`response.Text ?? string.Empty` four times, the `ChatOptions` shape
four times. The four adapters diverge only at **step 1's content**
(per-role prompt), **step 2's structure** (per-role user message),
**step 8** (per-role domain-type construction), and **step 10**
(per-role parse-failure policy).

The ADR-0046 / 0047 / 0050 lesson is **"depth in composition, not in
type proliferation"** — but the composition is missing here: each
adapter is a deep module *of mechanism*, and four parallel implementations
of the same mechanism is the structural duplicate ADR-0046's "router IS
an `IContentExtractor`" discipline rejected one level up.

Three follow-on motivations align with the duplicate:

- **Usage capture is currently zero.** None of the four adapters reads
  `ChatResponse.Usage.TotalTokenCount` — ADR-0051's `MaxBudgetTokens`
  cap can't see the tokens the brain spends because no code path
  surfaces them. The fix has to land in one place, not four.
- **Parse-failure retry is currently zero.** A model that returns a
  trailing comma sinks the whole adapter once. A one-shot
  `respond with valid JSON only` retry is the cheap defence; replicated
  four times it never gets written.
- **Tool-calling (ADR-0060) needs a seam.** The pivot from
  `ResponseFormat.Json` to `ChatOptions.Tools` + tool-call result
  parsing affects all four adapters. Adding it to one mechanism module
  is one change; adding it to four parallel implementations is four
  changes and four chances to drift.

### What this ADR does and does not move

**Moves into `LlmCall`:**

- The `ChatOptions` construction (Model / Temperature / MaxOutputTokens /
  ResponseFormat / Tools wiring).
- The `_chatClient.GetResponseAsync(...)` call site.
- `response.Text` extraction.
- The canonical `StripJsonFences` (one implementation).
- `JsonNode.Parse` + `JsonObject` cast.
- One inline parse-retry on JsonException (with a "respond with valid
  JSON only" reminder message — bounded at 1 retry, parse-failure-
  specific).
- `ChatResponse.Usage.TotalTokenCount` capture.
- The "extract tool-call result instead of text" path when descriptor
  has tools (ADR-0060's seam, shipped in the same release).
- Logging — categorised `WebReaper.AI.Llm.LlmCall`.

**Stays in the per-role adapter:**

- The role's system prompt content (the static string).
- The per-call user message construction (one `Func<TInput, string>`).
- The per-role response shape — descriptor's `ParseResponse` `Func<>`
  reads the `JsonElement` and returns the role's domain type.
- The per-role tool list (descriptor's `Tools` field; null on JSON-
  mode adapters).
- The role's failure policy — the adapter decides what to do with a
  `LlmCallResult` that signalled parse failure or that has no tool
  call to invoke.

This is the **descriptor + mechanism** split the ADR-0046 router used
between *predicate* and *composition* — the per-role policy is a record
of `Func<>`s; the mechanism is the class that runs them. Composition
over inheritance throughout the AI-native wave.

## Decision

Three pieces ship in the `WebReaper.AI` satellite under a new
`WebReaper.AI/Llm/` folder. The `IChatClient` binding stays exactly
where it is (ADR-0009 quarantine); core gains nothing.

### 1. `LlmCallDescriptor<TResponse>` — the per-role policy record

`WebReaper.AI/Llm/LlmCallDescriptor.cs`. Public record carrying the
per-role policies:

```csharp
public sealed record LlmCallDescriptor<TResponse>
{
    /// <summary>The role's invariant system prompt.</summary>
    public required string SystemPrompt { get; init; }

    /// <summary>Build the per-call user message from the role's input.
    /// Pure function; called once per <see cref="LlmCall{TResponse}.InvokeAsync"/>.</summary>
    public required Func<object, string> BuildUserMessage { get; init; }

    /// <summary>Parse the model's JSON response into the role's domain type.
    /// Receives the parsed JsonElement (post-fence-strip, post-retry).
    /// Throws on unrepairable parse failure — the mechanism translates
    /// the throw into a <c>LlmCallResult.Parsed = false</c>.</summary>
    public required Func<JsonElement, TResponse> ParseResponse { get; init; }

    /// <summary>Optional model id override (default null — chat client's
    /// configured model wins).</summary>
    public string? Model { get; init; }

    /// <summary>Sampling temperature (default 0).</summary>
    public float Temperature { get; init; } = 0.0f;

    /// <summary>Per-call response cap (default 4096).</summary>
    public int MaxResponseTokens { get; init; } = 4096;

    /// <summary>The chat response format. Default <c>ChatResponseFormat.Json</c>
    /// — the descriptor pattern keeps it overridable for the rare role
    /// (e.g. a future verifier returning a plain bool / number).</summary>
    public ChatResponseFormat ResponseFormat { get; init; } = ChatResponseFormat.Json;

    /// <summary>Optional tool list — the ADR-0060 seam. When non-null,
    /// the mechanism switches from JSON-mode parsing to tool-call
    /// parsing: <see cref="ParseResponse"/> is bypassed and a separate
    /// <see cref="ParseToolCall"/> is invoked instead.</summary>
    public IReadOnlyList<AIFunction>? Tools { get; init; }

    /// <summary>Required when <see cref="Tools"/> is non-null. The
    /// mechanism finds the first tool-call content in the response and
    /// passes <c>(toolName, argumentsJson)</c> to this delegate. ADR-0060.</summary>
    public Func<string, JsonElement, TResponse>? ParseToolCall { get; init; }
}
```

The `Tools` + `ParseToolCall` fields exist in v1 but only bind in
ADR-0060's slice — keeping the descriptor's surface stable across the
two ADRs avoids a second-record-shape churn.

### 2. `LlmCallResult<TResponse>` — the structured return

`WebReaper.AI/Llm/LlmCallResult.cs`. Public record:

```csharp
public sealed record LlmCallResult<TResponse>(
    TResponse? Value,
    bool Parsed,
    string? ParseFailureReason,
    long? TotalTokens,
    string RawResponse,
    int ParseRetries);
```

- `Value`: the parsed domain type (default(T) when `Parsed == false`).
- `Parsed`: success flag — if `false`, the adapter applies its role-
  specific failure policy (throw / return null / return Stop arm).
- `ParseFailureReason`: human-readable reason when `Parsed == false`.
- `TotalTokens`: `ChatResponse.Usage?.TotalTokenCount` when the chat
  client surfaces it; `null` otherwise. ADR-0051's `MaxBudgetTokens`
  reads this field directly.
- `RawResponse`: the unparsed text (or tool-arguments JSON), useful
  for logging / debugging.
- `ParseRetries`: 0 or 1 — how many retries the mechanism ran.

### 3. `LlmCall<TResponse>` — the mechanism class

`WebReaper.AI/Llm/LlmCall.cs`. Public sealed class:

```csharp
public sealed class LlmCall<TResponse>
{
    private readonly IChatClient _chatClient;
    private readonly LlmCallDescriptor<TResponse> _descriptor;
    private readonly ILogger _logger;

    public LlmCall(
        IChatClient chatClient,
        LlmCallDescriptor<TResponse> descriptor,
        ILogger<LlmCall<TResponse>>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(descriptor);
        _chatClient = chatClient;
        _descriptor = descriptor;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public async ValueTask<LlmCallResult<TResponse>> InvokeAsync(
        object input,
        CancellationToken cancellationToken = default)
    {
        // ... compose messages, call client, capture response,
        //     strip fences, parse with 1-retry on JsonException,
        //     return LlmCallResult.
    }
}
```

The single canonical `StripJsonFences` and the single canonical
`response.Text ?? ""` extraction live here. The retry policy is
inline-and-specific: catch one `JsonException`, append a one-line
"Your previous reply was not valid JSON; reply with valid JSON only.
Do not wrap in code fences." reminder, re-call once. On the second
failure return `Parsed = false` — the adapter decides whether that
becomes a throw, a null, or a Stop arm.

When `descriptor.Tools` is non-null (ADR-0060), `InvokeAsync` sets
`ChatOptions.Tools = descriptor.Tools` and parses the response's first
tool-call content (`ChatResponse.Messages[..].Contents` → first
`FunctionCallContent`) — `ParseToolCall(toolName, argumentsJson)` is
invoked instead of `ParseResponse`. The fence-strip / JSON-parse
retry code path is skipped — tool-call arguments are structured by
the SDK, no parse retry needed.

### 4. Per-role adapters shrink to descriptors

Each existing `Llm*` adapter constructs its `LlmCall<T>` from a
descriptor and delegates. Sketch for `LlmContentExtractor`:

```csharp
public sealed class LlmContentExtractor : IContentExtractor
{
    private readonly LlmCall<JsonObject> _call;
    private readonly LlmExtractorOptions _options;
    private readonly MarkdownContentExtractor _markdown = new();

    public LlmContentExtractor(IChatClient chatClient, LlmExtractorOptions? options = null)
    {
        _options = options ?? new LlmExtractorOptions();
        _call = new LlmCall<JsonObject>(chatClient, new()
        {
            SystemPrompt = _options.SystemPrompt ?? DefaultSystemPrompt,
            BuildUserMessage = input => BuildUserMessage((ExtractInput)input),
            ParseResponse = element => element.ToJsonObject(),
            Model = _options.Model,
            Temperature = _options.Temperature,
            MaxResponseTokens = _options.MaxTokens,
        });
    }

    public async Task<JsonObject> ExtractAsync(string document, Schema? schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var content = _options.UseMarkdownPreClean
            ? await PreCleanToMarkdownAsync(document)
            : document;
        var input = new ExtractInput(content, SchemaJsonSchemaBridge.ToJsonSchema(schema));
        var result = await _call.InvokeAsync(input);
        if (!result.Parsed)
            throw new InvalidOperationException(
                $"LLM extractor: {result.ParseFailureReason}");
        return result.Value!;
    }

    private readonly record struct ExtractInput(string Content, JsonObject JsonSchema);
}
```

~40 lines (down from ~140). `LlmActionResolver` becomes ~50 lines
(parse-failure becomes `null`); `LlmAgentBrain` becomes ~60 lines
(parse-failure becomes `AgentDecision.Stop`); `LlmSelectorRepairer`
becomes ~70 lines (parse-failure becomes `null`).

### Bounded scope (v1)

- **Per-role A/B / prompt-versioning.** No first-class support. A
  consumer wanting two prompts in flight builds two `LlmCall<T>`s with
  two descriptors and routes between them at the adapter layer.
- **Tokeniser-aware budget.** `TotalTokens` is whatever the chat
  client's `Usage` surfaces — the mechanism never tokenises text
  itself. (Locking in a tokeniser dep would re-do the ADR-0050
  satellite-zero-dep posture.)
- **Streaming.** `InvokeAsync` is non-streaming. The four current
  callers are non-streaming; ADR-0051 explicitly defers streaming
  brain decisions to v2.
- **Multi-turn / conversation state.** One `InvokeAsync` is one
  system + one user message; no history. Multi-turn adapters compose
  multiple `InvokeAsync` calls themselves — the mechanism doesn't
  carry state.

## Considered options

### Fork 1 — Module shape: stateful class vs. static function vs. pipeline

| Option | What | Verdict |
|---|---|---|
| (a) Stateful class with constructor-injected client + descriptor | `new LlmCall<T>(client, descriptor).InvokeAsync(input)`. The class carries the injected client and the per-role descriptor; one instance per adapter. | **Recommended.** Matches the four-adapter pattern — each adapter constructs one `LlmCall<T>` at construction time and reuses it across calls. Constructor injection is the project's stated DI shape. |
| (b) Static function | `LlmCall.InvokeAsync<T>(client, descriptor, input)`. No class. | Rejected. The descriptor is per-role; passing it on every call is repetitive boilerplate at every call site. Adapters would re-create the descriptor each invocation or have to cache it themselves. |
| (c) ASP-style delegate pipeline | `pipeline.Use(stripFences).Use(parseJson).Use(retry).Build()`. | Rejected. Speculative generality. The four steps (compose, call, strip, parse) are linear and load-bearing on their order; a pipeline abstraction is value the four callers do not need. |

### Fork 2 — Per-role policy: descriptor record vs. inheritance vs. options-object

| Option | What | Verdict |
|---|---|---|
| (a) Descriptor record with `Func<>` fields | `LlmCallDescriptor<T>` carries `SystemPrompt`, `BuildUserMessage`, `ParseResponse`. | **Recommended.** Composition over inheritance; matches the project's record-heavy style (`AgentDecision`, `CrawlOutcome`, `PageAction`, the satellite `*Options` records all sealed records). Descriptor is the per-role *configuration*; mechanism is the runtime. |
| (b) `abstract class LlmCall<T>` + per-role subclass overrides | `class LlmExtractorCall : LlmCall<JsonObject> { protected override string BuildUserMessage(...) }`. | Rejected. The four-method-override shape is exactly what records-with-Funcs avoid; subclassing a sealed mechanism class encodes the seam in vtable dispatch rather than in data. AOT-clean either way, but data wins on readability. |
| (c) Single options bag without per-role policies | `LlmCallOptions` holds prompt/temperature; the adapter calls `_chatClient.GetResponseAsync` directly. | Rejected. This is the *current* shape — exactly the duplicate this ADR exists to remove. |

### Fork 3 — Parse-retry policy

| Option | What | Verdict |
|---|---|---|
| (a) Zero retries | Adapter sees parse failure on first JsonException. | Rejected. Models occasionally emit trailing commas / partial outputs on a single bad sample; one retry is cheap defence and the firecrawl-research-digest pattern. |
| (b) One inline retry on `JsonException`, parse-failure-specific | `{ try parse; on JsonException, re-call with "valid JSON only" reminder; try parse once more; on second failure return Parsed=false }`. | **Recommended.** Bounded, predictable cost (max 2 model calls per `InvokeAsync`); decoupled from ADR-0026's network-retry. The reminder message is the canonical "respond with valid JSON only — do not wrap in code fences" string. |
| (c) Tie to ADR-0026 `IRetryPolicy` | Reuse the per-Job retry seam at the LLM call level. | Rejected. ADR-0026's retry wraps **network**-level transient failures (`Spider.CrawlAsync` throwing `HttpRequestException`); LLM-parse failures are not transient and don't need exponential backoff. Different concern, different layer. |

### Fork 4 — Prompt versioning / A/B testing

| Option | What | Verdict |
|---|---|---|
| (a) First-class prompt-version field on descriptor | `descriptor.PromptVariant = "v2"` + per-variant prompt selection. | Rejected (v2 deferral). One descriptor per role in v1; an A/B harness is a separate wrapper concern. |
| (b) Out of scope | No first-class support; consumers compose two `LlmCall<T>`s. | **Recommended.** A real A/B'er routes between two `LlmCall<T>`s themselves — same shape as ADR-0046's router routing between two `IContentExtractor`s. The pattern repeats; the abstraction doesn't need a second copy. |

### Fork 5 — Tool-call output mode (the ADR-0060 seam)

| Option | What | Verdict |
|---|---|---|
| (a) Separate `LlmToolCall<T>` mechanism class | Two parallel mechanisms — one for JSON-mode, one for tool-call mode. | Rejected. The two modes share 80% of the mechanism (compose / call / capture / Usage); duplicating it re-creates the very duplicate this ADR removes. |
| (b) One mechanism, `descriptor.Tools` is the selector | When `Tools != null` the mechanism takes the tool-call path; otherwise JSON-mode. `ParseResponse` is the JSON-mode parser, `ParseToolCall` is the tool-call parser. | **Recommended.** One module, both modes. The descriptor's shape stays stable; ADR-0060 plugs into the seam without re-shaping the descriptor record. |
| (c) Drop the seam from this ADR; ADR-0060 extends `LlmCall` later | Ship v1 of `LlmCall` JSON-only; widen the descriptor in 0060. | Rejected. Widening a public record breaks consumers — the seam needs to land in the same release as the mechanism. Both ADRs ship together; the descriptor's shape is forward-compatible from day one. |

### Fork 6 — Token-budget enforcement

| Option | What | Verdict |
|---|---|---|
| (a) Per-call cap in descriptor | `descriptor.MaxResponseTokens`; mechanism sets `ChatOptions.MaxOutputTokens`. | **Recommended.** Per-role default sits with the role's options record; bound is meaningful per call. |
| (b) Cumulative cap inside LlmCall | The mechanism enforces a cumulative token cap across all calls on this instance. | Rejected. Cumulative caps belong at the *engine* level (ADR-0051's `AgentEngineOptions.MaxBudgetTokens`); per-call mechanism is the wrong scope. |
| (c) No cap | Trust the chat client / model defaults. | Rejected. Cost-runaway is the named risk; ADR-0044's 4096-cap default has been the discipline since the AI-native wave. |

### Fork 7 — Logging

| Option | What | Verdict |
|---|---|---|
| (a) Own logger, categorised | `ILogger<LlmCall<TResponse>>` — distinct category per role-type | **Recommended.** Standard `Microsoft.Extensions.Logging` pattern; consumer filters per category. Token usage / parse retries / parse failures all log at Information / Warning. |
| (b) Shared logger from caller | The adapter passes its own logger to the mechanism. | Rejected. Hides the per-role granularity at the log-filter level; the mechanism's logging is a category callers may want to silence independently. |
| (c) No logging | `NullLogger`-only. | Rejected. Token usage and parse-retry counts are the two signals operators most often need; suppressing them by default would hurt the "what is my model spending" question. |

### Fork 8 — Public vs. internal

| Option | What | Verdict |
|---|---|---|
| (a) Public | Consumers can construct their own `LlmCall<T>`s. | **Recommended.** Consumer-authored LLM adapters (an `IPageProcessor` that asks the LLM to classify, a custom `IAgentBrain`, a future `ISchemaValidator` that semantically grades extraction) reuse the canonical mechanism — code-fence stripping, parse retry, Usage capture all consistent. Composability is the satellite's headline. |
| (b) Internal | Only the four built-in adapters use it; consumers re-implement their own. | Rejected. Re-introduces the duplicate one layer out; the cleaner ADR-0009-respecting move is "satellite-internal mechanism is a public seam for consumers of the satellite." |

## Consequences

- **The four `Llm*` adapters become thin descriptors.** ~140 lines →
  ~40-60 lines each. The role-specific code is *only* what's
  role-specific: the system prompt, the user-message build, the
  domain-type parse, the per-role parse-failure policy.
- **`StripJsonFences` exists once.** Currently in four places with
  identical implementations. The fence-strip bug fixed in three of
  four (and missed in one) would be one fix in one file.
- **Usage capture lights up four adapters at once.**
  `LlmCallResult.TotalTokens` is populated for every call;
  `AgentEngineOptions.MaxBudgetTokens` (ADR-0051) starts working.
- **Parse-failure retry lights up four adapters at once.** One bad
  comma in a model's JSON output no longer hard-fails the adapter on
  the first try; the bounded retry is parse-failure-specific (won't
  retry e.g. an HTTP timeout — that's ADR-0026's job).
- **Tool-calling becomes one descriptor field, not four adapter
  rewrites.** ADR-0060's pivot from JSON-mode to tool-call mode lands
  by setting `descriptor.Tools` + `descriptor.ParseToolCall` on the
  brain and the action resolver descriptors.
- **Consumer-authored AI adapters reuse the mechanism.** A third-party
  `WebReaper.AI.Anthropic.Extensions` package or a per-tenant
  `WebReaper.AI.MyOrg` satellite gets the same code-fence stripping,
  the same Usage capture, the same parse-failure shape as the built-in
  adapters — for free, by depending on `WebReaper.AI`'s public
  `LlmCall<T>`.
- **No core changes.** Lives entirely in `WebReaper.AI`; no `IContentExtractor`
  / `IAgentBrain` / `IActionResolver` / `ISelectorRepairer` surface touched.
- **CONTEXT.md** gains **LLM call** + **Llm call descriptor** terms in the
  AI-native section + a new Relationships line. The AI-native section is
  renamed from "ADR-0040..0051 wave" → "ADR-0040..0064 wave".
- **CLAUDE.md** gets a one-line gotcha — `LlmCall<TResponse>` is the
  satellite-internal mechanism module; consumer-authored AI adapters
  should reuse it for consistent fence-stripping / parse-retry / Usage
  capture rather than re-implementing.

## Implementation (slice, when accepted)

**Satellite — new module folder:**

1. **`WebReaper.AI/Llm/LlmCallDescriptor.cs`** — the public record.
2. **`WebReaper.AI/Llm/LlmCallResult.cs`** — the public result record.
3. **`WebReaper.AI/Llm/LlmCall.cs`** — the public mechanism class.

**Adapter rewrites — each loses ~80-100 lines of mechanism:**

4. **`WebReaper.AI/LlmContentExtractor.cs`** — re-cast as descriptor +
   `LlmCall<JsonObject>` delegate. Parse failure → throw
   `InvalidOperationException` with `ParseFailureReason`.
5. **`WebReaper.AI/LlmSelectorRepairer.cs`** — re-cast as descriptor +
   `LlmCall<Dictionary<string,string>>` delegate. Parse failure →
   return `null`.
6. **`WebReaper.AI/LlmActionResolver.cs`** — re-cast as descriptor +
   `LlmCall<PageAction?>` delegate. Parse failure → return `null`.
7. **`WebReaper.AI/LlmAgentBrain.cs`** — re-cast as descriptor +
   `LlmCall<AgentDecision>` delegate. Parse failure → return
   `AgentDecision.Stop { Reason = $"brain returned non-JSON: {reason}" }`.

**Tests — covering the mechanism in one place:**

8. **`WebReaper.Tests/WebReaper.AI.Tests/LlmCallTests.cs`** — pin every
   mechanism guarantee with a stub `IChatClient`:
   - Code-fence stripping (`` ```json {} ``` ``, `` ``` {} ``` ``,
     unwrapped JSON, leading whitespace, trailing whitespace, only-fence).
   - Parse-retry: first response is "{ trailing-comma, }" → second call
     issued with the reminder appended → success returned with
     `ParseRetries == 1`.
   - Parse-retry: both calls fail → `Parsed = false`, `ParseRetries == 1`,
     `ParseFailureReason` populated.
   - Usage capture: stub client returns `Usage.TotalTokenCount = 42` →
     `TotalTokens == 42`.
   - Usage absent: stub client returns null Usage → `TotalTokens == null`.
   - Tool-call mode: `descriptor.Tools` set → stub client returns a
     `FunctionCallContent` → `ParseToolCall` invoked, `ParseResponse`
     not invoked.
   - Tool-call mode without `ParseToolCall` set → constructor /
     `InvokeAsync` throws `InvalidOperationException` with actionable
     message.
   - Cancellation: `cancellationToken` propagates through the chat call.
9. **`WebReaper.Tests/WebReaper.AI.Tests/LlmContentExtractorTests.cs`** —
   existing tests stay green (now exercise the mechanism via the
   descriptor); add one regression covering the parse-retry path
   (model returns trailing comma → retry succeeds, no throw).
10. **`WebReaper.Tests/WebReaper.AI.Tests/LlmAgentBrainTests.cs`,
    `LlmActionResolverTests.cs`, `LlmSelectorRepairerTests.cs`** — same:
    existing tests stay green; one parse-retry regression each.

**Docs:**

11. **CONTEXT.md** — section rename "AI-native (the ADR-0040..0051 wave)"
    → "AI-native (the ADR-0040..0064 wave)"; new terms **LLM call** and
    **Llm call descriptor**; relationship line linking the mechanism to
    the four roles.
12. **CLAUDE.md** — AI-native paragraph extended "ADR-0040..0051" →
    "ADR-0040..0064"; new gotcha bullet on `LlmCall<TResponse>` as the
    mechanism for consumer-authored adapters.

### Guardrails (when implemented)

- `dotnet build WebReaper.sln` — 0 errors.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — all pass
  (no core surface touched; should be a no-op).
- `dotnet test WebReaper.Tests/WebReaper.AI.Tests` — all existing tests
  pass; new `LlmCallTests` pass; parse-retry regression on each adapter
  passes.
- `WebReaper.AotSmokeTest` — unchanged (mechanism lives in the
  non-AOT satellite).

## References

- ADR-0009 — registration seam + satellite pattern; the mechanism
  lives in the AI satellite, not core.
- ADR-0044 — LLM extractor satellite; the first adapter the mechanism
  consolidates.
- ADR-0046 — extraction router; the composition-over-types discipline
  this ADR mirrors one layer in.
- ADR-0047 — self-healing selectors; the second adapter the mechanism
  consolidates.
- ADR-0050 — semantic page actions / action resolver; the third
  adapter the mechanism consolidates.
- ADR-0051 — agent crawl driver; the fourth adapter the mechanism
  consolidates, **and** the consumer of `LlmCallResult.TotalTokens`
  via `AgentEngineOptions.MaxBudgetTokens`.
- ADR-0026 — retry policy seam; the network-retry layer the parse-
  retry deliberately stays distinct from.
- ADR-0060 — tool-calling brain + action resolver; the descriptor's
  `Tools` + `ParseToolCall` fields ship for this ADR but bind in 0060.
- ADR-0064 — `.UseAi(...)` policy; the consumer of the descriptor
  pattern at the registration layer.
