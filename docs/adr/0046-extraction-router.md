# `ExtractionRouter` — deterministic-first → fallback composition on the `IContentExtractor` seam

## Status

**Accepted — implementation complete** (2026-05-23; landed on branch
`ai-native-wave` off `origin/master`). **Slice 7 of the AI-native wave**.
Per [REPOSITIONING-PLAN §2.1](../REPOSITIONING-PLAN.md): "a new
`IExtractionRouter` seam composed *inside* the `IContentExtractor`
position." Additive — no Tier-1 break. Folds into the unreleased 10.0.0
wave; ships free, MIT.

## Context

The plan's §2.1 commits the routing decision: run the deterministic
`SchemaFold` first; on a validation failure, escalate to the LLM
fallback. The mechanism the plan calls out:

> A new `IExtractionRouter` seam composed *inside* the
> `IContentExtractor` position, not in `CrawlStep`. … Routing *is*
> real variation (deterministic pass, LLM fallback, self-heal,
> cached-selector demotion) → clears the ADR-0002 bar; it is a deep
> module implementing the post-ADR-0008 `IJsonContentParser` seam.

The post-ADR-0039 naming: the seam is `IContentExtractor`. The router is
*itself* an `IContentExtractor` — it does not introduce a new public
interface. The "I" in the plan's "`IExtractionRouter`" was the plan's
vocabulary; the implementation is a class implementing the existing
seam. Avoiding a seam-of-a-seam preserves ADR-0001/0002's discipline.

### What "validation" means

The deterministic fold's output on a missing field is governed by
ADR-0029's per-leaf policy:

- Selector matches nothing → `JsonValue.Create(string.Empty)` (empty
  string is the "matched zero nodes" marker).
- Coercion fails → field left unset (the JsonObject doesn't have the
  key).

The router's default validator declares the result *invalid* — and
escalates to the fallback — when any required schema leaf is empty
or absent. The default walks the schema recursively:

| Schema element | "Missing" iff |
|---|---|
| Leaf, non-list | absent OR (string-typed AND empty string) |
| Leaf, IsList | absent OR empty `JsonArray` |
| Container (Schema), non-list | absent |
| Container (Schema), IsList | absent OR empty `JsonArray` |

This default is conservative — a real "0" integer or a real "false"
boolean is *valid*; only string-empty/list-empty triggers the
fallback. Consumers needing different validation supply their own
predicate.

### Why this is one class, not two

The plan named "router" expecting a separate `IExtractionRouter`
interface plus an `ExtractionRouter` implementation plus the LLM as a
collaborator. The simplification (preserving the plan's intent):

- The router IS an `IContentExtractor`. It composes two
  `IContentExtractor`s (primary + fallback). The seam is the seam;
  composition is composition.
- A `Func<JsonObject, Schema?, bool>` predicate replaces a separate
  `IExtractionValidator` interface. One-method interfaces are
  delegates in C#.

Nothing in the plan's "deep module" wording requires more shape than
this — the depth is in the composition logic, not a separate type.

## Decision

Three pieces; the seam stays unchanged.

### 1. `ExtractionRouter` — the composing adapter

[WebReaper/Core/Parser/Concrete/ExtractionRouter.cs](../../WebReaper/Core/Parser/Concrete/ExtractionRouter.cs).
Public class implementing `IContentExtractor`. Constructor:

```csharp
public ExtractionRouter(
    IContentExtractor primary,
    IContentExtractor fallback,
    Func<JsonObject, Schema?, bool>? isValid = null,
    ILogger? logger = null)
```

`ExtractAsync` runs `primary`, runs the `isValid` predicate, returns
the primary result if valid, runs `fallback` otherwise.

### 2. Default validator: `SchemaSatisfiedValidator`

[WebReaper/Core/Parser/Concrete/SchemaSatisfiedValidator.cs](../../WebReaper/Core/Parser/Concrete/SchemaSatisfiedValidator.cs).
A static class with one static method
`bool IsSatisfied(JsonObject result, Schema? schema)` that walks the
schema and tests per the table above. Used as the default predicate
when the consumer doesn't supply one.

### 3. `WithFallbackExtractor` on `ScraperEngineBuilder`

A convenience that wraps the currently-registered (or default)
`IContentExtractor` with an `ExtractionRouter`:

```csharp
public ScraperEngineBuilder WithFallbackExtractor(
    IContentExtractor fallback,
    Func<JsonObject, Schema?, bool>? isValid = null)
```

For the LLM-specific case, the `WebReaper.AI` satellite ships a
sugared overload `WithLlmFallback(IChatClient, LlmExtractorOptions?)`
that wires the LLM extractor as the fallback.

### Bounded scope

- **Not** a multi-stage router. v1 is *primary → fallback*. A chain of
  N extractors is shapeable later by composing multiple
  `ExtractionRouter`s; no new seam needed.
- **Not** the self-heal mechanism (ADR-0047). Self-heal is a different
  composition: after the LLM produces a successful extraction,
  *learned* selectors are cached and used by the deterministic
  primary on subsequent runs. The router doesn't know about caching;
  self-heal is a layer above.
- **Not** a budget governor. Token-budget enforcement happens in the
  consumer's `IChatClient` (e.g. an OpenAI quota), not here. The
  plan's "budget governor" wording is honored by the LLM extractor's
  `MaxTokens` option (ADR-0044).
- **No streaming.** The fallback runs synchronously after the primary
  fails — same as the bare LlmContentExtractor.

## Considered options

### (a) A separate `IExtractionRouter` interface — rejected

Two interfaces for one piece of composition; the router IS an
`IContentExtractor` and gains nothing from an extra type.

### (b) Mid-pipeline validation via a page processor — rejected

`IPageProcessor` (ADR-0038) runs *after* the extractor; the
fallback-on-failure logic needs to swap the extractor itself, not
post-process the record. A processor seeing "this field is empty"
cannot recover by re-extracting with a different strategy.

### (c) Hardcoded "always escalate" policy — rejected

Wastes LLM budget on every page. The deterministic fold succeeds the
majority of the time on stable templates.

### (d) Cache the validator decision per URL — rejected (deferred)

Self-heal's job (ADR-0047). The router stays stateless.

### (e) Make the validator a class implementing an interface — rejected

`Func<JsonObject, Schema?, bool>` is one method; a delegate is the
honest shape. Same discipline as ADR-0036's "shape from the second
adapter, not for it" — the second-validator-shape doesn't exist yet.

## Consequences

- **The plan's §2.1 ships.** Deterministic-first → fallback routing is
  the documented composition; the wedge is in place for the LLM
  extractor (ADR-0044) to be reached only when the cheap path fails.
- **The IContentExtractor seam stays a seam, not a seam-of-a-seam.**
  Routing is composition; the public surface is unchanged.
- **The fallback is any IContentExtractor.** LLM in the common case;
  could be another deterministic fold against a different schema,
  could be a custom strategy. The router doesn't care.
- **Self-heal (ADR-0047) has a home to layer on.** A future "use the
  cached selector instead" extractor sits *inside* the primary slot;
  the router doesn't change.
- **CONTEXT.md** gains an **Extraction router** term + relationship
  line.

## Implementation

Landed on `ai-native-wave`:

1. **`ExtractionRouter.cs`** — new, public, `IContentExtractor`-
   implementing.
2. **`SchemaSatisfiedValidator.cs`** — new, public; default predicate.
3. **`ScraperEngineBuilder.WithFallbackExtractor`** — new method.
4. **`WebReaper.AI/LlmExtractorRegistration.WithLlmFallback`** — new
   sugar (composes the LLM extractor as the fallback).
5. **Tests** — `ExtractionRouterTests` pins primary-served-when-valid,
   fallback-served-when-invalid, default validator behaviour,
   custom predicate behaviour, schema-recursion (lists, containers).
6. **CONTEXT.md** — term + relationship line.

### Guardrails (run on the branch tip)

- `dotnet build WebReaper.sln` — 0 errors.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — all baseline +
  new ExtractionRouterTests pass.
- `WebReaper.AotSmokeTest` — no change required; routing is
  composition of AOT-clean pieces, AOT smoke remains green.

## References

- ADR-0002 — Schema fold + node-backend seam; the primary the router
  composes with.
- ADR-0029 — coercion-failure policy; what "missing field" *means* in
  the fold's output, and therefore what the default validator checks
  for.
- ADR-0039 — `IContentExtractor`; the seam the router implements.
- ADR-0040 — Markdown extractor; can serve as a fallback too (e.g.
  "if structured extraction fails, return cleaned Markdown").
- ADR-0044 — LLM extractor; the *typical* fallback the router pairs
  with.
- ADR-0047 — self-healing selectors; layers caching + selector-propose
  *inside* the primary slot of a router.
- REPOSITIONING-PLAN §2.1 / §2.9 — the routing decision + budget
  governor wording this ADR cashes.
