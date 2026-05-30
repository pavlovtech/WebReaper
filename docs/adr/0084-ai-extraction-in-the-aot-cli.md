# AI extraction in the AOT CLI (`--prompt` / `--infer`)

## Status

proposed

## Context

The CLI (`WebReaper.Cli`, ADR-0043) is deterministic-only and 100% LLM-free. It references just `WebReaper` plus `WebReaper.Cdp`, sets `PublishAot=true`, and promotes the AOT/trim IL warning set to errors. All AI extraction has lived in library code or the `WebReaper.AI` satellite, which ADR-0009 deliberately quarantined out of the AOT story (its own note: "the satellite isn't AOT-required").

Two facts reopened that decision.

Product: one-command, schema-free extraction (a natural-language prompt, no hand-written selectors) is now the baseline expectation. Firecrawl, Crawl4AI, and ScrapeGraphAI all ship it. But a competitor sweep found that almost nobody ships it inside a native single-binary CLI. AI runs server-side (Firecrawl's CLI is a thin cloud client) or in a Python/Node runtime (Crawl4AI). The only native-binary-with-baked-AI tool found was `webclaw` (Rust, early-stage). A .NET-AOT CLI with built-in BYO-key AI extraction is a near-empty, on-strategy niche.

Feasibility: a spike (AOT-publishing the real satellite with the CLI's exact error set) proved `WebReaper.AI` fails AOT today on exactly two reflection-JSON lines (`LlmCallDescriptor`'s default `JsonSerializerOptions.Default`, and `LlmCall`'s tool-args `JsonSerializer.Serialize`), not on anything fundamental. `Microsoft.Extensions.AI.Abstractions` itself compiled AOT-clean, and the team had already hand-rolled `HandRolledAIFunction` to avoid the reflection function factory.

## Decision

Add two AI extraction flags to `scrape` and `crawl`:

- **`--prompt "<instruction>"`** gives **Prompt extraction** (new): the LLM reads each page and extracts per a natural-language instruction, returning whatever JSON fits. Schema-free, one LLM call per page. A new `IContentExtractor` strategy plus a fourth `ICrawlSeed` terminal `ExtractWithPrompt(instruction)` (proposed name), sibling to `Extract` / `AsMarkdown` / `ExtractInferred`.
- **`--infer "<goal>"`** reuses the existing **Learned-schema content extractor** (ADR-0067): infer a schema once, then deterministic-fold the rest, roughly one LLM call for a whole crawl.

Both run by baking a hardened `WebReaper.AI` into the AOT CLI and supplying it a raw-HTTP, AOT-safe, OpenAI-compatible `IChatClient` from a new **`WebReaper.AI.Http`** satellite, wired through the satellite's existing `WithLlmExtractor` / `WithLlmSchemaInferrer`.

This supersedes the ADR-0009 quarantine for the satellite's own code: `WebReaper.AI` becomes AOT-grade (the two reflection-JSON sites move to System.Text.Json source generation, and a new AOT smoke test bakes the satellite so it can never regress). The quarantine's other half still holds: concrete provider SDKs remain the consumer's concern and are never baked; the CLI's BYO `IChatClient` is the AOT-safe raw-HTTP one.

## Considered options (the grilling pass)

- **`--prompt` vs `--infer`:** ship both as distinct, honest strategies. Rejected silently making `crawl --prompt` infer-once behind the user's back; it is cheaper but surprises the user with empty records on structurally heterogeneous pages (the ADR-0067/0069 failure mode). `--prompt` is robust, per-page, and scales in cost; `--infer` is cheap, infers once, and is brittle. Two flags, two semantics.
- **Bake the satellite vs CLI-local LLM plumbing:** bake. Reimplementing `--infer` (inferrer, `LearnedSchemaContentExtractor`, validator, ADR-0069 re-inference) inside the CLI would rewrite ADR-0067 and 0069. Reuse wins the moment `--infer` is in scope, and the spike proved baking is AOT-viable.
- **LLM client location:** a reusable `WebReaper.AI.Http` satellite (an AOT-safe BYO `IChatClient`, which the .NET ecosystem otherwise lacks since M.E.AI's own OpenAI client is not AOT-tested), not CLI-internal. Protocol is OpenAI Chat Completions, the lingua franca (OpenAI, Ollama, OpenRouter, vLLM, Anthropic-compat).
- **Config:** explicit only, no auto-detect. `--model` plus `--llm-url` (or `WEBREAPER_LLM_*`); the API key is env-only, never a flag; missing config fails actionable. Ollama-first auto-detect was rejected because silently using a weak local model is a worse surprise than a clear error.
- **Cost guard:** `crawl --prompt` over roughly 50 pages prints an estimate and prompts `[y/N]`; `--yes` skips it (mirroring the ADR-0056 stealth prompt). `scrape --prompt` never prompts, since it is one call.
- **Output:** `--output-dir` writes one file per page (default `./webreaper-out/` in the cwd, never `~/Documents`, which breaks pipes, is absent in CI/headless/containers, and is OS-localized). stdout stays the default. A TTY-gated stderr path hint, plus an opt-in `--open` flag rather than an interactive prompt, since GUI presence cannot be reliably detected over SSH.
- **AOT hardening scope:** harden the whole satellite (both reflection-JSON lines plus the baking smoke test), not just the extraction path. A half-AOT-clean satellite is a trap: a later `.WithLlmBrain` wiring would break at publish.

## Consequences

- The CLI gains its first LLM dependency; `Microsoft.Extensions.AI.Abstractions` enters the AOT binary (spike-verified clean). Binary size grows.
- `WebReaper.AI` must stay AOT-clean permanently, enforced by a new smoke test that bakes it. This is a real reversal of the ADR-0009 "AI satellite is AOT-excluded" posture for the satellite's own code.
- One new package to ship and version in lockstep: `WebReaper.AI.Http` (the release `CANDIDATES` list).
- Monetization: one-command AI extraction in the free binary is BYO-key, so the user pays inference. It is convenience, not COGS, and stays consistent with the local-first, MIT, open-core posture; the paid surface (managed proxies/stealth, Cloud scheduling) is unaffected.
- The autonomous `AgentEngine` is explicitly out of scope here; this ADR covers on-page extraction only. Hardening the whole satellite keeps the agent-in-CLI door open for a later ADR, but the "the CLI's caller is already a brain" objection still stands against it.
