# WebReaper.SchemaInferenceShowcase

Showcase example for the v10.0.0 schema-inference dock — ADR-0067 (the
seam + wrapper + seed terminal), ADR-0068 (`.UseAi(...)` one-line
policy), and ADR-0069 (validator-driven re-inference).

Funnel-side companion to [`WebReaper.AiNativeShowcase`](../WebReaper.AiNativeShowcase/)
(which covers ADR-0040..0049 from the original AI-native wave).

## Run

All three scenarios use a deterministic in-process `StubChatClient` so
the example runs offline; the production swap is any
[`Microsoft.Extensions.AI.IChatClient`](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai)
adapter — OpenAI, Anthropic, Ollama, Azure AI, …

```bash
# ADR-0067 — à la carte: .ExtractInferred(goal).WithLlmSchemaInferrer(client)
dotnet run --project Examples/WebReaper.SchemaInferenceShowcase -- alacarte

# ADR-0068 — one-line policy: .ExtractInferred(goal).UseAi(client, Inferred)
dotnet run --project Examples/WebReaper.SchemaInferenceShowcase -- useai

# ADR-0069 — validator-driven re-inference: default / opt-out / cost cap
dotnet run --project Examples/WebReaper.SchemaInferenceShowcase -- reinfer
```

## What each scenario demonstrates

### `alacarte` (ADR-0067)

The à la carte registration shape:

```csharp
var engine = await ScraperEngineBuilder
    .Crawl("https://example.com/")
    .ExtractInferred(goal: "page title and summary")
    .WithLlmSchemaInferrer(chatClient)
    .WriteToConsole()
    .BuildAsync();
```

First page pays the LLM once; the proposed `Schema` caches on the
[`LearnedSchemaContentExtractor`](../../WebReaper/Core/Parser/Concrete/LearnedSchemaContentExtractor.cs)
wrapper; every subsequent page runs the deterministic
[`SchemaFold`](../../WebReaper/Core/Parser/Concrete/SchemaFold.cs)
against the cached schema. The cheapest dock of the project-level
proposer-validator pattern — one LLM call per crawl.

### `useai` (ADR-0068)

The one-line policy equivalent:

```csharp
var engine = await ScraperEngineBuilder
    .Crawl("https://example.com/")
    .ExtractInferred(goal: "page title and summary")
    .UseAi(chatClient, new AiOptions(Policy: AiPolicyMode.Inferred))
    .WriteToConsole()
    .BuildAsync();
```

`AiPolicyMode.Inferred` is the fifth canned arm. It wires
`WithLlmSchemaInferrer + WithLlmActionResolver` (the orthogonal
action surface — useful regardless of extraction strategy).
Mutually exclusive with `Recommended` / `LlmPrimary` /
`ExtractionOnly` (those register an `IContentExtractor` that would
shadow `LearnedSchemaContentExtractor`).

On the agent builder `.UseAi(Inferred)` throws actionably — the
brain proposes its own schemas per `AgentDecision.Extract(schema)`,
so a separate inferrer is structurally redundant.

### `reinfer` (ADR-0069)

The wrapper's validator-driven re-inference, demonstrated by
constructing `LearnedSchemaContentExtractor` directly with scripted
stubs (easier to see the count progression than driving a real
multi-page crawl — the unit tests pin the same mechanics across the
full matrix). Three sub-demos:

1. **Default** (`ReInferAfterFailures: 3`): three consecutive
   validator failures drop the cached schema; the next call
   re-infers. A wrong first-page inference auto-heals — the crawl
   stops producing empty records.
2. **Opt-out** (`ReInferAfterFailures: 0`): preserves the ADR-0067
   v1 trust-the-cache behaviour. The wrapper never drops the cached
   schema regardless of validator verdicts. The strict no-extra-LLM-
   spend posture.
3. **Cost cap** (`MaxReInferencesPerInstance: 1`): bounds total LLM
   spend on one wrapper instance. After the cap, further failures
   keep the stale schema and log at Warning. The unattended / CI /
   cron guardrail.

The wrapper's public
[`ReInferencesUsed`](../../WebReaper/Core/Parser/Concrete/LearnedSchemaContentExtractor.cs)
property is the cost-observability surface.

## How the funnel composes

| Strategy | Seed terminal | Policy one-liner |
|---|---|---|
| Schema-driven | `.Extract(schema)` | `.UseAi(client)` |
| LLM-primary  | `.Extract(schema)` | `.UseAi(client, new AiOptions(Policy: LlmPrimary))` |
| Extraction-only | `.Extract(schema)` | `.UseAi(client, new AiOptions(Policy: ExtractionOnly))` |
| Markdown | `.AsMarkdown()` | (no `.UseAi(...)` needed) |
| Inferred | `.ExtractInferred(goal?)` | `.UseAi(client, new AiOptions(Policy: Inferred))` |

`WebReaper.AiNativeShowcase` covers the first four;
`WebReaper.SchemaInferenceShowcase` is the fifth row.
