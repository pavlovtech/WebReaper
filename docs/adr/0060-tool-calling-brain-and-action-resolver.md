# Tool-calling brain + action resolver — `AIFunction` over `ResponseFormat.Json`

## Status

**Proposed** (2026-05-24). Second ADR of the post-AI-native-wave
deepening campaign; depends on ADR-0059's `LlmCall<TResponse>`
mechanism module. Pivots `LlmAgentBrain` (ADR-0051) and
`LlmActionResolver` (ADR-0050) from JSON-mode parsing to
Microsoft.Extensions.AI's `AIFunction` + `ChatOptions.Tools`. The
closed-sum becomes load-bearing on both sides of the seam — the model
calls a tool, the SDK delivers a structured arm, no JSON-parse path.
Breaking change for the satellite (v10.x major). The JSON-mode parsing
path is dropped; chat clients without tool-call support are
unsupported. Folds into the same v10.x release as ADR-0059.

## Context

The brain and the action resolver are *both* closed-sum decision
returners:

- `AgentDecision` has four arms: `Extract(Schema)`, `Follow(Url)`,
  `Act(PageAction)`, `Stop()` — every arm carries a `Reason`.
- `PageAction` has seven arms (post-ADR-0050): `Click(selector)`,
  `Wait(ms)`, `WaitForSelector(selector, timeoutMs)`,
  `WaitForNetworkIdle()`, `ScrollToEnd()`, `EvaluateExpression(js)`,
  `SemanticAct(intent)`.

The current implementations ask the LLM for a JSON object with a
discriminator field (`"type": "extract"`, `"kind": "click"`) and hand-
parse. The shape is awkward in three concrete ways:

1. **Discriminator drift.** `LlmAgentBrain`'s prompt names "extract /
   follow / act / stop"; the parser switches on `"type"`. A renamed arm
   means *prose change* (the prompt), *code change* (the switch), and
   *test change*. Three places, one closed sum.
2. **Hand-parse-and-validate.** `ParseAction` in `LlmActionResolver`
   reads `obj["kind"]`, branches on a string, validates `obj["selector"]`
   is non-empty, etc. Every arm parses its own field-presence rules in
   procedural code — exactly the per-leaf duplication the project's
   closed-sum discipline elsewhere structurally eliminates.
3. **No schema-level structure.** The prompt enumerates the four shapes
   in prose; nothing checks the model emitted one of them. Unknown
   `"type"`s land in the `default:` branch and silently become a
   `Stop("brain returned unknown decision type 'X'")`. The closed sum
   is closed in C# but open at the LLM boundary.

`Microsoft.Extensions.AI` ships `AIFunction` + `ChatOptions.Tools` on
the GA layer (`Microsoft.Extensions.AI.Abstractions`). The shape:

- Register N `AIFunction`s, each with a name, a description, a JSON
  Schema for its arguments, and an invocation delegate.
- Set `ChatOptions.Tools = aiFunctions`.
- The provider (OpenAI / Anthropic / Gemini / Ollama with a tool-using
  model) returns a structured `FunctionCallContent` with the tool's
  name and the model-supplied arguments as a JSON object — already
  parsed by the SDK.
- The arguments JSON is validated against the tool's argument schema
  *by the provider* (modern models).

This is structurally what the brain and the action resolver need:
each arm is a tool, the model picks one, the SDK gives back the
arm-shape with its parameters typed. **The closed sum is load-
bearing at the LLM boundary the same way it is in C#**: an unknown
arm becomes structurally impossible at the seam, not a `default:`
branch we hope is unreachable.

### Composition with ADR-0059

ADR-0059's `LlmCallDescriptor<TResponse>` ships with `Tools` +
`ParseToolCall` fields exactly so this ADR's pivot is one descriptor
field, not four adapter rewrites. The plumbing:

| Descriptor field | Brain | Action resolver |
|---|---|---|
| `Tools` | 4 `AIFunction`s — one per `AgentDecision` arm | 6 `AIFunction`s — one per concrete (non-`SemanticAct`) `PageAction` arm |
| `ParseToolCall(name, args)` | switch on tool name → construct `AgentDecision` arm with typed args | switch on tool name → construct `PageAction` arm with typed args |
| `SystemPrompt` | "Pick one tool to indicate your next step." | "Pick one tool to indicate the concrete action." |
| `ParseResponse` | unused (tools mode) | unused (tools mode) |

The mechanism (ADR-0059) takes the tool-call path when `Tools` is
non-null; the descriptor is the per-role policy. One place to plumb
both. Zero JSON-parse code in the per-role adapter.

### Breaking-change posture

The current `LlmAgentBrain` parses JSON-mode responses; tool-calling
is not a *superset* of JSON-mode parsing — chat clients whose providers
don't support tool calling can't return `FunctionCallContent`. The
options:

- **Keep JSON-mode as a fallback path** alongside tool-calling.
  Doubles the per-role surface (one mechanism, two paths), defeats the
  ADR-0059 deduplication, and forces a runtime "did this provider
  support tools? if not, fall back" question that nobody on the
  satellite path wants to ask.
- **Break in v10.x.** The AI-native version cut explicitly allowed
  breaking change; the user said so. Microsoft.Extensions.AI's
  `IChatClient` GA surface supports tools on the GA layer; if the
  underlying provider doesn't, the chat client wrapper does (or the
  consumer picked a provider they shouldn't have). This is "supported
  providers support tool-calling" — same shape as
  "supported transports support all seven `PageAction` arms"
  (ADR-0053's Playwright posture, closing ADR-0004's four-arm
  Puppeteer gap).

This ADR picks the second option. The version-cut posture is
intentional.

### Arm packaging for `PageAction`: nested vs. flat

`PageAction` has seven arms, one of which (`SemanticAct`) is the
*input* to the action resolver and *must not* be a returnable tool
(see fork 8). The remaining six are concrete dispatchable arms:

- Nested: one tool `Act` with parameters `{ "kind": "click", "selector": ... }` — re-creates the discriminator-field hand-parse the JSON-mode shape had. Defeats the pivot's purpose.
- Flat: six tools, one per arm — `ActClick(selector)`, `ActWait(ms)`, `ActWaitForSelector(selector, timeoutMs)`, `ActWaitForNetworkIdle()`, `ActScrollToEnd()`, `ActEvaluate(expression)`. The model picks one; the SDK validates the args; the resolver constructs the corresponding `PageAction` record from the SDK-parsed arguments.

Flat is the structurally honest shape — every arm is a tool. ADR-0035's
closed-sum discipline matches at the LLM boundary.

The brain's `Act` arm — distinct from the resolver's situation — wraps
a `PageAction` instance. Rather than the brain proposing an entire
nested `PageAction` JSON, the brain registers four arms — `Extract`,
`Follow`, `Act` (with parameters `{actionName: string, ...}`), `Stop`.
For the brain's `Act`, the *same* flat packaging applies — six concrete
tools `ActClick` / `ActWait` / etc., and *not* `ActSemanticAct`
(brain's `Act(SemanticAct)` is supported via a flat `ActSemanticAct`
tool — distinct from the resolver's tool list, see fork 8). This keeps
the brain's surface flat and the closed sum load-bearing.

So the brain ships **nine** tools (`Extract`, `Follow`, `Stop`, plus
six concrete `Act*` arms plus `ActSemanticAct`), and the resolver ships
**six** tools (the concrete `Act*` arms only; no `ActSemanticAct`).

## Decision

Five pieces. All five land in the `WebReaper.AI` satellite; no core
surface changes. The bedrock — ADR-0059's `LlmCall` — is the carrier.

### 1. Per-arm `AIFunction` factories

`WebReaper.AI/Tools/AgentDecisionTools.cs` — internal static class:

```csharp
internal static class AgentDecisionTools
{
    public static IReadOnlyList<AIFunction> ForBrain() => new[]
    {
        ExtractTool(),    // -> AgentDecision.Extract
        FollowTool(),     // -> AgentDecision.Follow
        StopTool(),       // -> AgentDecision.Stop
        ActClickTool(),
        ActWaitTool(),
        ActWaitForSelectorTool(),
        ActWaitForNetworkIdleTool(),
        ActScrollToEndTool(),
        ActEvaluateTool(),
        ActSemanticActTool(),
    };

    private static AIFunction ExtractTool() =>
        // Hand-rolled JSON Schema for the argument shape:
        //   { "schema": <flat-field-name-to-selector map>, "reason": "<why>" }
        new HandRolledAIFunction(
            name: "Extract",
            description: "Extract a record from the current page using the supplied flat field-to-selector schema.",
            parametersSchema: new JsonObject {
                ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["schema"] = new JsonObject {
                        ["type"] = "object",
                        ["additionalProperties"] = new JsonObject { ["type"] = "string" },
                        ["description"] = "Map of field name to CSS selector."
                    },
                    ["reason"] = new JsonObject { ["type"] = "string" }
                },
                ["required"] = new JsonArray { "schema", "reason" }
            });

    // ... one factory method per arm ...
}
```

The `HandRolledAIFunction` is `WebReaper.AI/Tools/HandRolledAIFunction.cs`
— a small internal `AIFunction` subclass whose `JsonSchema` returns a
pre-built `JsonObject`. Hand-rolling (not `AIFunctionFactory.Create(MethodInfo)`)
keeps the satellite AOT-trivially-safe — no reflection over .NET methods
to build the schema (see fork 4).

The same file ships `ForResolver()` — same shape, but only the six
concrete `Act*` arms; no `ActSemanticAct` (fork 8 — the structural
loop-prevention).

### 2. `LlmAgentBrain` becomes a tool-call descriptor

The descriptor's `Tools = AgentDecisionTools.ForBrain()`,
`ParseToolCall = (name, args) => ParseDecisionTool(name, args)`. The
`ParseDecisionTool` switch:

```csharp
private static AgentDecision ParseDecisionTool(string toolName, JsonElement args)
{
    var reason = args.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";
    return toolName switch
    {
        "Extract"              => new AgentDecision.Extract(BuildFlatSchema(args)) { Reason = reason },
        "Follow"               => new AgentDecision.Follow(args.GetProperty("url").GetString()!) { Reason = reason },
        "Stop"                 => new AgentDecision.Stop { Reason = reason },
        "ActClick"             => new AgentDecision.Act(new PageAction.Click(args.GetProperty("selector").GetString()!)) { Reason = reason },
        "ActWait"              => new AgentDecision.Act(new PageAction.Wait(args.GetProperty("ms").GetInt32())) { Reason = reason },
        "ActWaitForSelector"   => new AgentDecision.Act(new PageAction.WaitForSelector(
                                       args.GetProperty("selector").GetString()!,
                                       args.GetProperty("timeoutMs").GetInt32())) { Reason = reason },
        "ActWaitForNetworkIdle"=> new AgentDecision.Act(new PageAction.WaitForNetworkIdle()) { Reason = reason },
        "ActScrollToEnd"       => new AgentDecision.Act(new PageAction.ScrollToEnd()) { Reason = reason },
        "ActEvaluate"          => new AgentDecision.Act(new PageAction.EvaluateExpression(args.GetProperty("expression").GetString()!)) { Reason = reason },
        "ActSemanticAct"       => new AgentDecision.Act(new PageAction.SemanticAct(args.GetProperty("intent").GetString()!)) { Reason = reason },
        _                      => new AgentDecision.Stop { Reason = $"brain called unregistered tool '{toolName}'" }
    };
}
```

The system prompt becomes shorter — the tool list *is* the schema, no
JSON-shape prose required:

```text
You are an autonomous web-scraping agent reasoning step-by-step on
pages of a single site to satisfy the user's goal. At each step you
observe the current page (rendered to Markdown), the candidate links,
your prior decisions and visited URLs. Pick ONE tool to indicate your
next step. Always supply a 'reason' explaining your choice. Prefer
Follow over Act when a link will do. Pick Follow URLs FROM the
candidate list. Don't propose a visited URL. Stop when the goal is
satisfied or the page set is exhausted without progress.
```

### 3. `LlmActionResolver` becomes a tool-call descriptor

Descriptor's `Tools = AgentDecisionTools.ForResolver()` (six concrete
arms — no `ActSemanticAct`, ever). Parse-tool-call switch:

```csharp
private static PageAction? ParseActionTool(string toolName, JsonElement args)
    => toolName switch
    {
        "ActClick"              => new PageAction.Click(args.GetProperty("selector").GetString()!),
        "ActWait"               => new PageAction.Wait(args.GetProperty("ms").GetInt32()),
        "ActWaitForSelector"    => new PageAction.WaitForSelector(
                                       args.GetProperty("selector").GetString()!,
                                       args.GetProperty("timeoutMs").GetInt32()),
        "ActWaitForNetworkIdle" => new PageAction.WaitForNetworkIdle(),
        "ActScrollToEnd"        => new PageAction.ScrollToEnd(),
        "ActEvaluate"           => new PageAction.EvaluateExpression(args.GetProperty("expression").GetString()!),
        _                       => null
    };
```

Resolver returns `null` when the model called no tool — `LlmCall`'s
"no-tool-call" case is delivered as `Parsed = false`. The transport's
`SemanticActResolutionException` continues to wrap it.

### 4. Engine "no tool call" / "invalid arm" handling

The mechanism (ADR-0059) signals "no tool call" via `Parsed = false`
when `Tools` is set but no `FunctionCallContent` came back. Each
adapter handles it per role policy:

- **Brain**: `Parsed = false` → `AgentDecision.Stop { Reason = "brain returned no tool call" }`. Then the engine logs the bad shape and terminates.
- **Resolver**: `Parsed = false` → `null`. The transport throws `SemanticActResolutionException("resolver returned no tool call")`.
- **Engine-side validation** (visited-link rejection on Follow, MaxSteps cap) is unchanged — it is the engine's job, not the brain's.

### 5. Drop the JSON-mode `ParseResponse` path on these two adapters

`LlmAgentBrain` and `LlmActionResolver` no longer set `ParseResponse`
in their descriptors. The mechanism observes `Tools != null` and skips
`ParseResponse`. `LlmContentExtractor` and `LlmSelectorRepairer` keep
JSON-mode (their outputs are *content*, not closed-sum decisions; the
tool-call shape doesn't fit). The mechanism supports both modes —
the descriptor decides per-role.

### Bounded scope (v1)

- **No fallback to JSON-mode for chat clients without tool support.**
  Breaking change posture; v10.x major. A consumer with such a chat
  client either upgrades (most providers added tools years ago) or
  swaps to a different `IChatClient` wrapper. The exception is loud
  and actionable — `LlmCallResult.ParseFailureReason = "chat client
  returned no tool call; provider may not support tool-calling"`.
- **Brain's `Extract` arm carries a flat schema only.** Nested
  schemas (objects-within-objects, lists-of-objects) stay the
  ADR-0051 v2 deferral; the tool's argument schema is
  `additionalProperties: { type: string }` — field name to CSS
  selector. Same shape as ADR-0051's `ParseFlatSchema`, mechanism-
  unchanged.
- **No streaming tool calls.** `DecideAsync` / `ResolveAsync` are
  non-streaming in v1 (ADR-0051 fork j, unchanged).
- **No parallel tool calls.** `IChatClient`'s tool-call API allows
  multi-tool-per-response on some providers; this ADR takes the
  first tool call and ignores the rest. The closed-sum is one
  decision per step.

## Considered options

### Fork 1 — Per-arm `AIFunction` vs. one mega-function

| Option | What | Verdict |
|---|---|---|
| (a) Per-arm `AIFunction` | One tool per arm; model picks the tool. | **Recommended.** Matches the closed-sum lineage; one tool = one arm; the SDK validates the per-arm args against the per-arm schema. |
| (b) One mega-tool `Decide` with a discriminator parameter | Single tool `Decide({type, ...})`; the discriminator is a parameter the model fills. | Rejected. Re-creates the JSON-mode discriminator-and-hand-parse exactly the way the current code does — defeats the pivot. |

### Fork 2 — `PageAction` packaging in brain's `Act`: nested vs. flat

| Option | What | Verdict |
|---|---|---|
| (a) Nested — one `Act` tool with a nested `action` object | `Act({ action: { kind: "click", selector: "..." } })`. | Rejected. Same hand-parse problem one level down; the nested arg schema is the JSON discriminator the pivot eliminates. Also: providers vary on nested-object validation rigour. |
| (b) Flat — six `Act*` tools (and seventh `ActSemanticAct`) | The brain's tool list is `Extract`, `Follow`, `Stop`, plus seven `Act*` arms; ten tools total. | **Recommended.** Flat keeps every arm's schema simple; the SDK validates each. |

### Fork 3 — Keep JSON-mode adapter as fallback for tool-unsupporting chat clients

| Option | What | Verdict |
|---|---|---|
| (a) Drop JSON-mode on brain + resolver | Tool-calling required. Breaking change. | **Recommended.** Tool-calling is GA in `Microsoft.Extensions.AI`; supported providers support it. A consumer with a tool-unsupporting `IChatClient` upgrades or wraps. |
| (b) Per-adapter `ToolCallingPreferred` knob | Brain tries tool calling; if `Parsed = false` because no tool came back, retry with JSON-mode + parse. | Rejected. Doubles the per-role surface; the runtime "did this work?" question is exactly the noise tool-calling exists to avoid. |
| (c) Provider-shape detection | Sniff `IChatClient` at construction; if it doesn't surface a tool-calling capability, register the JSON-mode descriptor instead. | Rejected. `IChatClient` doesn't expose a tool-capability bit (the GA surface assumes tools-are-supported); detection is brittle. |

### Fork 4 — `AIFunction` parameter schema: hand-rolled vs. reflection-built

| Option | What | Verdict |
|---|---|---|
| (a) Hand-rolled `JsonObject` per arm | Build the parameter schema as a `JsonObject` literal in the factory method. | **Recommended.** AOT-trivially-safe; no reflection over .NET types. The schema is small (3-5 properties per arm); the code is exactly as readable as the JSON-mode prompt was. Reuses `SchemaJsonSchemaBridge`'s `JsonObject`-building style. |
| (b) `AIFunctionFactory.Create(MethodInfo)` | Reflect a `static AgentDecision Extract(string[] schema, string reason)` method; the SDK reads parameter types via reflection. | Rejected. Reflection breaks AOT (ADR-0009 quarantine — the satellite is non-AOT, but the hand-rolled discipline keeps the option open if AOT compatibility is ever desired here). Also: schema-from-`MethodInfo` requires the SDK's reflection-based JSON Schema generator, which is the satellite's stated avoid. |
| (c) Source-gen the `AIFunction`s via a small Roslyn generator | `[Tool]` on a method; generator emits the factory + the schema. | Rejected (v2 deferral). Speculative generality; the hand-rolled file is ten arms in ~150 lines. Source-gen wins on N > 10. |

### Fork 5 — Brain's "no tool call returned" handling

| Option | What | Verdict |
|---|---|---|
| (a) Default to `Stop("brain returned no tool call")` and log | The adapter returns the Stop arm; engine logs + terminates next iteration. | **Recommended.** Matches the existing parse-failure shape (`AgentDecision.Stop` on every brain-side malformity). The engine's `Stop` handling is already audited. |
| (b) Throw `InvalidOperationException` | Hard-fail the agent run. | Rejected. ADR-0051's posture is *brain failures don't crash the engine*; same here. |
| (c) Retry the brain | Re-call with an "actually call one of the tools" reminder. | Rejected. `LlmCall`'s parse-retry covers JSON parse failures, not "ignored the tool list" — distinct failure mode. A second retry shape is creep. |

### Fork 6 — Streaming tool calls

| Option | What | Verdict |
|---|---|---|
| (a) Non-streaming | Single response per `InvokeAsync`. | **Recommended.** v1, unchanged from ADR-0051 fork j. |
| (b) Stream-and-build | The SDK supports streaming `FunctionCallContent`s; build the arg JSON as it arrives. | Rejected (v2 deferral). The brain's output is one structured decision; streaming buys nothing for the latency-bound use case. |

### Fork 7 — Tool-call validation: brain self-checks or engine enforces

| Option | What | Verdict |
|---|---|---|
| (a) Engine enforces, brain doesn't know | The engine rejects an `AgentDecision.Follow(url)` whose `url` isn't in `CandidateUrls`; the brain has no awareness of the rejection mechanism. | **Recommended.** Matches ADR-0051 fork 12. The brain's tool list doesn't constrain `url` to candidates (would be a schema with `enum` of candidate URLs — schema bloat for every step); the engine has the authoritative state. |
| (b) Tool schema's `enum` enforces candidate URLs | `Follow`'s `url` parameter is `{ "type": "string", "enum": [<every-candidate-URL>] }`. | Rejected. Schema bloat that scales with candidate count; the tool's parameter schema would change every step and the SDK would re-validate every call. The engine-side check is one-line and authoritative. |

### Fork 8 — Resolver's "must not return SemanticAct" rule

| Option | What | Verdict |
|---|---|---|
| (a) Resolver registers six tools — no `ActSemanticAct` ever | The closed sum is closed at the resolver tool list; the model structurally cannot return SemanticAct. | **Recommended.** Loop-prevention is the *tool list*, not runtime validation. ADR-0050's "the resolver never returns a `SemanticAct` arm" comment becomes a structural property. |
| (b) Register all seven; runtime-reject `SemanticAct` in `ParseActionTool` | Same loop prevention, runtime. | Rejected. The structural shape is cheaper and more honest — the model never sees the loop-able tool. |
| (c) `ParseActionTool` allows `ActSemanticAct` but the transport drops it | Tolerate, log, return `null`. | Rejected. Same loop-prevention problem at a worse layer. |

## Consequences

- **The closed sum is load-bearing at the LLM boundary, not just in
  C#.** The brain cannot return an unknown `"type"`; the resolver
  cannot return an unsupported `"kind"`; the SDK validates the per-arm
  args against the per-arm schema before they reach `ParseToolCall`.
- **Discriminator drift is eliminated.** Adding an eighth
  `PageAction` arm: add one `AIFunction` factory + one
  `ParseActionTool` switch arm. Renaming an arm: rename two siblings
  in one file. The prompt prose is short and stable.
- **JSON-parse code disappears from the brain + resolver.** No
  `JsonNode.Parse`, no `obj["kind"]` lookups, no `null`-handling on
  every property read. SDK delivers a typed arm.
- **Breaking change for v10.x.** Chat clients whose providers don't
  support tool calling no longer work. The exception is loud and
  actionable — `LlmCallResult.ParseFailureReason` carries the message.
  CLAUDE.md gets a one-line gotcha.
- **Composability with ADR-0059.** Zero per-adapter mechanism work;
  the descriptor's `Tools` + `ParseToolCall` fields are the only
  per-role surface that changed. Consumer-authored brains / resolvers
  picking the tool-calling shape pay the same descriptor cost.
- **`ParseResponse` stays in the descriptor for content/repair
  adapters.** `LlmContentExtractor` and `LlmSelectorRepairer` continue
  JSON-mode parsing — their outputs are *content*, not closed-sum
  decisions. One mechanism, two modes; the descriptor picks per role.
- **CONTEXT.md** gains a **Brain tool registry** term — the closed-sum-
  as-tools mapping — plus a relationship line noting the structural
  closed-sum-at-the-seam property.
- **CLAUDE.md** gains a gotcha line on tool-calling now being the
  structural path for brain + action resolver; the JSON-mode parsing
  path is gone in v10.x; chat clients without tool support are
  unsupported.

## Bounded scope (v1)

The named v2 deferrals:

- **(a) Streaming tool calls** — non-streaming in v1.
- **(b) Tool-call source-gen** — hand-rolled `AIFunction` factories
  in v1; a Roslyn generator earns its keep at N > 10 tools per
  registry.
- **(c) Multi-tool-per-response** — first tool call wins; subsequent
  ignored.
- **(d) Nested schemas in `Extract` arm** — flat field-to-selector
  map in v1 (matches ADR-0051's v1 posture).
- **(e) Provider-shape detection / JSON-mode fallback** — unsupported
  in v1; consumers pick a tool-supporting `IChatClient`.

## Implementation (slice, when accepted)

**Satellite — new tool-list module:**

1. **`WebReaper.AI/Tools/AgentDecisionTools.cs`** — `ForBrain()` /
   `ForResolver()` factories. Internal.
2. **`WebReaper.AI/Tools/HandRolledAIFunction.cs`** — internal
   `AIFunction` subclass; carries the pre-built parameter schema, the
   tool name, the description.

**Adapter rewrites — descriptor changes only (ADR-0059 carries the
mechanism):**

3. **`WebReaper.AI/LlmAgentBrain.cs`** — descriptor's `Tools =
   AgentDecisionTools.ForBrain()`, `ParseToolCall = ParseBrainTool`,
   `ParseResponse = null`. Shortened system prompt. The
   `ParseDecision` / `ParseFlatSchema` / `ParseAction` JSON helpers
   are deleted.
4. **`WebReaper.AI/LlmActionResolver.cs`** — descriptor's `Tools =
   AgentDecisionTools.ForResolver()`, `ParseToolCall = ParseActionTool`,
   `ParseResponse = null`. Shortened system prompt. The `ParseArm` JSON
   helper is deleted.

**Tests:**

5. **`WebReaper.Tests/WebReaper.AI.Tests/AgentDecisionToolsTests.cs`** —
   pin the brain registry shape (10 tools, the expected names, the
   expected arg schemas) and the resolver registry shape (6 tools, no
   `ActSemanticAct`). Schema-snapshot tests: every tool's schema is
   pinned to a stable JSON shape.
6. **`WebReaper.Tests/WebReaper.AI.Tests/LlmAgentBrainTests.cs`** —
   existing JSON-mode tests rewritten as tool-call tests; stub
   `IChatClient` returns a `FunctionCallContent` for each arm; assert
   the parsed `AgentDecision` shape. Specific regressions: model
   returns no tool call → `Stop("brain returned no tool call")`;
   model calls an unregistered tool name → `Stop("brain called
   unregistered tool 'X'")`.
7. **`WebReaper.Tests/WebReaper.AI.Tests/LlmActionResolverTests.cs`** —
   same: existing JSON-mode tests rewritten; assert SemanticAct can
   never round-trip (resolver's tool list lacks the arm); assert
   unknown tool name → `null`.

**Docs:**

8. **CONTEXT.md** — new **Brain tool registry** term; relationship
   line noting closed-sum-at-the-seam.
9. **CLAUDE.md** — gotcha line on tool-calling-only posture for brain
   + resolver; chat clients without tool support are unsupported.

### Guardrails (when implemented)

- `dotnet build WebReaper.sln` — 0 errors.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — all pass (no
  core surface touched).
- `dotnet test WebReaper.Tests/WebReaper.AI.Tests` — existing JSON-mode
  test count holds (rewritten); new tool-shape pinning tests pass.
- `WebReaper.AotSmokeTest` — unchanged (mechanism + tools live in the
  non-AOT satellite).

## References

- ADR-0001 — closed-sum pattern; the discipline this ADR pushes to
  the LLM boundary.
- ADR-0035 — `PageAction` closed sum; the seven arms the resolver's
  tool list flattens (minus `SemanticAct`).
- ADR-0050 — semantic page actions / action resolver; the role this
  ADR pivots to tool-calling.
- ADR-0051 — agent crawl driver; the role this ADR pivots to tool-
  calling, including the `AgentDecision` closed sum.
- ADR-0053 — Playwright satellite; the precedent for "supported
  providers support the modern path" version-cut posture.
- ADR-0059 — `LlmCall<TResponse>`; the mechanism this ADR plugs
  `Tools` + `ParseToolCall` into.
- ADR-0061 — `LastDecisionOutcome`; the brain's tool-call cycle now
  has a richer feedback signal between decisions.
