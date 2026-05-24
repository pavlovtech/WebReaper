# `WebReaper.AI` — an LLM-backed `IContentExtractor` satellite bound to `Microsoft.Extensions.AI.Abstractions`

## Status

**Accepted — implementation complete** (2026-05-23; landed on branch
`ai-native-wave` off `origin/master`). **Slice 5 of the AI-native wave**
([REPOSITIONING-PLAN §2.4](../REPOSITIONING-PLAN.md)). New satellite
project per ADR-0009 — core stays dependency-light and AOT-clean. Folds
into the unreleased 10.0.0 wave; ships free, MIT.

## Context

The repositioning plan's §2.4 commits the LLM substrate binding to
**`Microsoft.Extensions.AI.Abstractions`** — Microsoft's durable GA
layer carrying `IChatClient` / `IEmbeddingGenerator` — explicitly **not**
to Semantic Kernel or Agent Framework naming. The reasoning (recorded
in the plan): the Abstractions layer is the lower, more stable contract
that survives framework whiplash; Semantic Kernel deprecation /
Agent Framework rebranding are the failure modes a 1.0-bound library
must avoid.

ADR-0039 named `IContentExtractor` with explicit prose that an LLM
extractor is a second adapter implementing the seam directly. ADR-0040
shipped a Markdown adapter; this ADR is the LLM one — the third adapter,
the one the seam was named for.

### Where the satellite sits

ADR-0009 established the registration-seam + satellite pattern: heavy
dependencies live in per-technology satellite packages
(`WebReaper.{Cosmos,Mongo,Redis,AzureServiceBus,Puppeteer}`); the core
stays dependency-light and Native-AOT-clean. `Microsoft.Extensions.AI`
brings transitive deps and reflection-driven serialisation that would
break the core's AOT-clean guarantee — exactly the case the satellite
pattern was designed for.

New satellite: **`WebReaper.AI`**. Depends only on
`Microsoft.Extensions.AI.Abstractions` (the abstractions; not the
helpers package). The consumer brings their own concrete `IChatClient`
— OpenAI's `OpenAIClient`, Anthropic via a wrapper, an Ollama client, a
test stub. That's the BYO-model story the plan calls for.

### What the LLM extractor does

`LlmContentExtractor : IContentExtractor`. Given a loaded `document`
and a `Schema`:

1. Pre-clean the document (Markdown by default — ADR-0040's extractor —
   to drop chrome and reduce token count; raw HTML when the consumer
   opts in).
2. Convert the `Schema` to a JSON Schema (the standard LLM
   structured-output spec). The conversion mirrors the fold's
   `DataType` → JSON Schema `type`; `IsList` becomes `array` with
   `items`; nested `Schema` becomes nested `object`.
3. Compose two `ChatMessage`s — a focused system prompt ("extract
   structured data … output only JSON matching the schema") and a user
   prompt with the schema + the cleaned content.
4. Call `IChatClient.GetResponseAsync` with `ChatOptions.ResponseFormat
   = ChatResponseFormatJson.Json` (or a JSON-Schema-bound variant if
   the consumer's chat client supports it, transparently fallback to
   plain JSON otherwise).
5. Parse the response text as `JsonObject`; strip any markdown code
   fences the model returned wrapping the JSON.
6. Return — sinks consume the `JsonObject` the same way the
   deterministic fold's output is consumed.

### Token-budget discipline

LLM extraction is the most expensive path in the entire pipeline. The
satellite makes the cost knobs explicit and the defaults *cheap*:

- **Default input: Markdown** (cleaned), not raw HTML. On a typical
  editorial page, that's ~10× fewer tokens. The MarkdownContentExtractor
  (ADR-0040) is reused — same dependency, same cleanup.
- **Default `MaxTokens`**: 4096 (response cap). Most extracted records
  are <1KB; 4096 is a comfortable ceiling.
- **No streaming.** Streaming is useful for chat; extraction is
  one-shot, the parser needs the whole response.
- **No retries inside the extractor.** ADR-0026's
  `IRetryPolicy` is the Crawl driver's retry seam — wrapping the
  extractor in additional retry is a misplaced concern. The retry
  policy already covers the extractor's call (it wraps
  `Spider.CrawlAsync`).

### What this ADR does NOT include

- **The router (ADR-0046).** That's the deterministic-first → LLM
  composition; this ADR ships just the LLM adapter. A consumer who
  wants pure LLM extraction registers the LLM extractor directly via
  `WithContentExtractor`; the router lands next.
- **Self-healing (ADR-0047).** Selector-repair-with-LLM is a separate
  feature using `IChatClient` for a different prompt. It composes on
  top of the router, not the bare extractor.
- **Embedding-based document chunking** for very large pages. v1 sends
  the full Markdown; if a page exceeds the model's context window, the
  caller gets a model error. Chunking + re-aggregation is a real
  future feature behind a `ChunkingPolicy` knob; out of scope here.
- **AOT cleanliness.** The satellite is deliberately not `IsAotCompatible`
  (mirrors `WebReaper.Redis`'s ADR-0009 stance). The consumer's chosen
  chat client makes the AOT call; quarantining the choice in the
  satellite preserves the core's AOT-clean guarantee.

## Decision

Five concrete pieces, one satellite, ADR-0039 seam preserved.

### 1. `WebReaper.AI` — new satellite project

[WebReaper.AI/](../../WebReaper.AI/). PackageReference:
`Microsoft.Extensions.AI.Abstractions` (only). ProjectReference:
WebReaper core. No PublishAot; ADR-0009 quarantine.

### 2. `LlmContentExtractor` — the adapter

[WebReaper.AI/LlmContentExtractor.cs](../../WebReaper.AI/LlmContentExtractor.cs).
Public, `IContentExtractor`-implementing, takes `IChatClient` +
`LlmExtractorOptions`. Schema is required (it's the structured-output
spec); throws `ArgumentNullException` on null, matching the
SchemaFold's contract.

### 3. `LlmExtractorOptions` — the knobs

```csharp
public sealed record LlmExtractorOptions(
    string? Model = null,           // chat-options model id
    bool UseMarkdownPreClean = true,// run Markdown extractor first
    int MaxTokens = 4096,           // response cap
    float Temperature = 0.0f,       // deterministic by default
    string? SystemPrompt = null);   // override the default
```

`Model = null` means the chat client's default — Microsoft.Extensions.AI's
`ChatOptions.ModelId` accepts null. Temperature defaults to 0 because
extraction is a deterministic task; a non-zero temperature is an
opt-in for fuzzier sites.

### 4. `Schema` → JSON Schema converter

[WebReaper.AI/SchemaJsonSchemaBridge.cs](../../WebReaper.AI/SchemaJsonSchemaBridge.cs).
A pure function: recursive walk over `Schema` / `SchemaElement` →
`JsonObject` JSON Schema. The selector text is intentionally dropped
(LLMs don't extract by selector); only the field name, type, and
nesting are preserved. List elements (`IsList`) become `array` of
`items: { type: T }`.

### 5. `ScraperEngineBuilder.WithLlmExtractor(IChatClient, LlmExtractorOptions?)`

An extension method in `WebReaper.AI` namespace (the satellite
registration pattern, ADR-0009). Wires the `LlmContentExtractor` via
the existing `WithContentExtractor` seam:

```csharp
namespace WebReaper.AI;
public static class LlmExtractorRegistration
{
    public static ScraperEngineBuilder WithLlmExtractor(
        this ScraperEngineBuilder builder,
        IChatClient chatClient,
        LlmExtractorOptions? options = null)
        => builder.WithContentExtractor(new LlmContentExtractor(chatClient, options));
}
```

Consumer-facing:

```csharp
using Microsoft.Extensions.AI;
using WebReaper.AI;

var chatClient = new OpenAI.Chat.ChatClient("gpt-4o-mini", apiKey).AsIChatClient();

var engine = await ScraperEngineBuilder
    .Crawl("https://example.com")
    .Extract(schema)
    .WithLlmExtractor(chatClient)
    .WriteToConsole()
    .BuildAsync();

await engine.RunAsync();
```

### Considered options

#### (a) Bind to `Microsoft.SemanticKernel` instead — rejected

The plan §2.4 explicitly rejected SK naming for framework-whiplash
risk. `Microsoft.Extensions.AI.Abstractions` is the durable lower
layer Microsoft itself targets for cross-stack interop.

#### (b) Bake the LLM extractor into core — rejected

It carries Microsoft.Extensions.AI transitive deps; baking it in
breaks ADR-0009's "core stays dependency-light and AOT-clean."

#### (c) Ship a default `IChatClient` (e.g. OpenAI) — rejected

Locks the satellite to one provider. The Abstractions layer's point is
provider-neutral; the consumer brings their preferred model.

#### (d) Implement chunking in v1 — rejected (deferred)

A 200KB Markdown blob exceeds GPT-4o's 128K context — but the median
extracted page is <10KB Markdown. Chunking adds re-aggregation
complexity for an edge case; v1 ships single-shot, with the error
surface honestly reporting context-length-exceeded to the caller.

#### (e) Validate the LLM response against the schema before returning — rejected (deferred)

The router (ADR-0046) is the home for validation; the bare extractor
trusts its model. The router composes the deterministic-first check
*and* an LLM-only validation pass.

#### (f) Send raw HTML by default — rejected

~10× more tokens for typically zero accuracy gain. The chrome
(nav/footer/scripts) confuses the model on average and costs more.
Opt-out exists via `UseMarkdownPreClean = false` for the rare
edge case.

## Consequences

- **The IContentExtractor seam has its third adapter.** ADR-0039
  predicted the LLM extractor exactly; ADR-0040 added Markdown; this
  ADR adds LLM. The seam is now demonstrably multi-strategy.
- **The router (ADR-0046) has its second backend.** Det-first →
  LLM-fallback composes the SchemaFold and the LlmContentExtractor;
  with both adapters present, the router lands cleanly.
- **The repositioning plan's §2.4 ships.** Microsoft.Extensions.AI
  binding is the locked decision; this ADR cashes it.
- **The core stays dependency-light and AOT-clean.** The Microsoft.
  Extensions.AI dependency lives in the satellite per ADR-0009.
- **BYO model.** No transitive lock-in to OpenAI / Anthropic / etc.;
  the consumer's `IChatClient` choice is their concern.
- **CONTEXT.md** gains an **LLM extractor** term + the satellite
  relationship line.
- **CLAUDE.md** gains a one-line gotcha — `WithLlmExtractor` requires
  the WebReaper.AI satellite + the consumer's chat-client package.

## Implementation

Landed on `ai-native-wave`:

1. **`WebReaper.AI/WebReaper.AI.csproj`** — new satellite (no
   PublishAot per ADR-0009 quarantine).
2. **`WebReaper.AI/LlmContentExtractor.cs`** — the adapter.
3. **`WebReaper.AI/LlmExtractorOptions.cs`** — the knobs record.
4. **`WebReaper.AI/SchemaJsonSchemaBridge.cs`** — Schema → JSON Schema
   converter.
5. **`WebReaper.AI/LlmExtractorRegistration.cs`** —
   `WithLlmExtractor` extension method on `ScraperEngineBuilder`.
6. **`WebReaper.Tests/WebReaper.AI.Tests/`** — new tests using a stub
   `IChatClient`, covering schema-to-JSON-Schema conversion (the pure
   function), prompt composition, code-fence stripping, and
   end-to-end extraction-against-a-stub-chat.
7. **CONTEXT.md** — terms + relationship line.

### Guardrails (run on the branch tip)

- `dotnet build WebReaper.sln` — 0 errors.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — baseline passes.
- `dotnet test WebReaper.Tests/WebReaper.AI.Tests` — new tests pass.
- Core `WebReaper.AotSmokeTest` — unchanged; the satellite is not
  AOT-required per ADR-0009.

## References

- ADR-0009 — the registration seam + satellite pattern this ADR uses.
- ADR-0023 — Tier-1 / Tier-2 doc contract; `LlmContentExtractor` is
  the satellite's documented surface.
- ADR-0026 — retry policy; the LLM call is already covered by the
  Crawl driver's per-Job retry wrapper.
- ADR-0029 — coercion-failure policy; the LLM extractor cannot hit
  Coerce (it doesn't fold a Schema) so the policy is strategy-
  irrelevant here, exactly as for the Markdown extractor (ADR-0040).
- ADR-0039 — `IContentExtractor`; the seam this ADR lands on.
- ADR-0040 — `MarkdownContentExtractor`; this ADR reuses it as the
  default pre-clean step.
- ADR-0046 — the router; the deterministic-first composition layer
  this ADR's LLM extractor backs.
- REPOSITIONING-PLAN §2.4 — the locked `Microsoft.Extensions.AI`
  binding decision this ADR cashes.
