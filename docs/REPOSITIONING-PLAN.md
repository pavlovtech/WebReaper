# WebReaper Repositioning — Architecture & Implementation Plan

> **Status:** Draft for owner review · **Date:** 2026-05-18 (Rev 4)
> **Source:** `research/` — the three analyst reports (now treated as *hypotheses*) **plus `research/proven-cases-and-base-rates.md` (the evidence base, authoritative where they conflict)** + full codebase audit. Architecture produced via the Plan architect agent, verified against actual files under this repo.
> **Deliverable:** a sequenced, ADR-grounded plan. No code. All claims grounded in files read under this repository.
>
> **Rev 2 (2026-05-17):** MCP demoted from headline wedge to one managed-transport adapter; the AOT CLI + agent skill is the primitive wedge (§2.5).
> **Rev 3 (2026-05-17) — revenue-spine pivot:** evidence (`research/proven-cases-and-base-rates.md`) shows a monetised solo .NET scraping *library* / `Pro.*` tier has ~zero precedent and a documented exact-analog failure (**AbotX**). The proven model is adopted: **the entire library + CLI + skill is free & permissive — the *funnel*; the monetised product is a separate, bootstrapped, usage-priced hosted unblocking/extraction *API*** (ScrapingBee/ScraperAPI shape; ZZZ-Projects-style funnel). §1, §3, §4, §6, §7, §8 rewritten; §2 architecture preserved with tier semantics corrected.
> **Rev 4 (2026-05-18) — reconciled against shipped 7.0.0:** Rev 1–3 were drafted *around* the 7.0.0 ship and are now partly retired by it. **7.0.0 already banked the plan's load-bearing technical de-risking**: ADR-0008 (System.Text.Json typed pipeline; core fully Newtonsoft-free; whole-core Native-AOT zero-warning; `JObject` path *removed*, not `[Obsolete]`; `WebReaper.AotSmokeTest` exists) and ADR-0009 (the registration-seam + per-technology satellite-package split — `WebReaper.{Cosmos,Mongo,Redis,AzureServiceBus,Puppeteer}`). Consequences folded into this rev: (a) the plan's proposed ADR ledger is corrected against the real numbering: 0008 *and* 0009 shipped; ADR-0008's out-of-scope bullet **reserves 0010 for the `[ScrapeSchema]` source generator and 0017 for the GPL→MIT relicense**; **0011 is held by an in-progress, plan-external ADR** (a file-persistence decision, unrelated to and not tracked by this plan); so the plan's to-author ledger is **ADR-0010 (`[ScrapeSchema]`), ADR-0017 (relicense), and ADR-0012–0016 / 0018–0021** for the rest (0011 skipped — external) — see §5; (b) §2.12 and Phase 0/1's "AOT/STJ blast-radius spike / execute the migration / major SemVer bump" are marked **done in 7.0.0**, not pending; (c) Phase 0 reduces to its **two genuinely-remaining gates** — GPL→MIT relicense consent and the owner-only market-discovery interviews; (d) the GPL→MIT contributor audit (§3) is run and folded in. Strategy (the funnel→hosted-API thesis) is unchanged; only sequencing, status, and ADR numbers are corrected.

## Locked strategic decisions

1. **Reposition** (not rewrite): keep WebReaper's ADR-driven seam core; evolve it into an AI-native .NET extraction runtime — *deterministic-first, LLM-fallback, browser-when-needed, agent-native, AOT-clean*.
2. **Funnel + hosted API** (supersedes "open-core + commercial Pro"): the library/CLI/skill is **100% free, permissive (MIT)** — the credibility/SEO/adoption funnel. The monetised product is a **separate hosted, usage-priced unblocking/extraction API** built on the same engine. No `WebReaper.Pro.*` commercial NuGet tier.
3. **Phased anti-bot**: in the free library, a pluggable transport seam (bring-your-own provider/proxies). Managed unblocking + (later) TLS/HTTP-2 fingerprint impersonation are **capabilities of the hosted service**, not shipped library code.
4. **Bootstrapped, content-led viability** (supersedes "enterprise commercial viability"): grow like ScrapingBee/ScraperAPI — developer-sold API, acquired via deep technical content; bootstrapped, no VC; realistic shape ≈ $1K→$8K MRR→$1M ARR over ~15 months, exitable. Not venture-scale.

### Load-bearing assumption — now resolved by evidence

Rev 1–2 rested on "a narrow paid .NET niche is defensible," which the pressure-test flagged as fragile. Rev 3 replaces the assumption with **base rates** (`research/proven-cases-and-base-rates.md`): the only repeated solo/tiny-team revenue model *in scraping* is the developer-sold usage-priced API grown via content (ScrapingBee → $5M ARR/8-fig exit; ScraperAPI → $400K MRR/acquired); the only proven solo *.NET* funnel is ZZZ Projects (free HtmlAgilityPack → separate paid product). The remaining real risk is no longer "is the niche real" but **"does the free funnel convert to API revenue, and can hosted infra be run lean"** — addressed by a Phase-0 market-discovery gate (§4) and the lean-ops sequencing.

---

## 1. Positioning statement

**WebReaper becomes the best *free, permissive* .NET extraction stack — library + AOT CLI + agent skill — and that stack is the funnel for a separate, bootstrapped, usage-priced hosted unblocking/extraction API.** The free stack is *deterministic-first, LLM-fallback, browser-when-needed, agent-native, AOT-clean*; it exists to earn adoption, credibility and search traffic among .NET developers and data teams (the ZZZ-Projects/HtmlAgilityPack pattern). The **product** is the hosted API: an HTTP endpoint (with a first-class .NET SDK + the CLI as its on-ramp) that handles the billable operational pain — managed proxy rotation/unblocking, JS rendering at scale, retries, reliability, ongoing anti-bot upkeep — priced per successful request (the ScrapingBee/ScraperAPI/Zyte model). The wedge that the *funnel* uses to stand out in a Python-dominated field is genuinely .NET-native quality: **source-generated `[ScrapeSchema]` typed extraction that Pydantic+instructor structurally cannot match, an AOT single-binary CLI, and a first-class agent skill** — none of which the incumbents (ScrapingBee/ScraperAPI/Zyte) offer to .NET teams. Headline funnel proof point: **.NET 10 AOT cold-start ≈ 940 ms vs ≈ 6,680 ms regular .NET 8** (reframed as binary-size/memory/single-file/no-runtime-install for on-prem; not led as a serverless claim — see §6).

**What it is NOT:** not a monetised library / no `WebReaper.Pro.*` tier (the AbotX precedent — a paid commercial .NET crawler that failed and reverted to free); not a residential-proxy business (the hosted API *wraps* providers as its backend, it does not resell IPs); not a no-code/visual product (import.io/Octoparse/ParseHub ceiling); not donations-funded (proven to fail as income); not Python-feature-parity; not bulk-training crawling; not VC-scale.

---

## 2. Target architecture

The same engine powers **both** the free funnel (library/CLI/skill) and the hosted API service. "Commercial" below means *the hosted service the team operates*, never a paid NuGet. Every free-library extension point that an integration could plug into (unblockers, observability sink, governance) is a **pluggable seam in the OSS with a no-op/BYO default**; the hosted service is simply the highest-quality managed implementation behind those seams, run as a SaaS — it is not gated code in the package.

The discipline of ADR-0001/0002 — *a seam must have real variation; reject indirection-without-variation* — governs every "new seam vs. existing seam" call below.

### 2.1 Deterministic-first → LLM-fallback routing — **NEW SEAM (justified)**

A new `IExtractionRouter` seam composed *inside* the `IContentParser` position, not in `CrawlStep`. `CrawlStep.StepAsync` (`WebReaper/Core/Crawling/Concrete/CrawlStep.cs:36`) calls `_contentParser.ParseToJsonAsync` for the empty-chain target-page arm; routing in `CrawlStep` would violate ADR-0001 (the crawl step is the pure page-category decision, not extensible) and recreate the rejected `ICrawlStepStrategy` shape. `CrawlOutcome` stays a closed 3-arm sum. Routing *is* real variation (deterministic pass, LLM fallback, self-heal, cached-selector demotion) → clears the ADR-0002 bar; it is a deep module implementing the post-ADR-0008 `IJsonContentParser` seam (`WebReaper/Core/Parser/Abstract/IJsonContentParser.cs`, `Task<JsonObject> ParseToJsonAsync(...)`). Run the deterministic `SchemaContentParser<TNode>` fold first; on typed-schema validation failure, escalate to the LLM backend. **ADR-0012**; additive, minor SemVer. Free OSS (BYO model key).

### 2.2 Self-healing selectors — fits existing seams

LLM proposes selectors → validate deterministically by re-running the fold against the live document + typed schema → persist and demote the site back to the deterministic path. Storage reuses the **keyed blob store** (ADR-0003/0005, `WebReaper/DataAccess/IKeyedBlobStore.cs`) via a new "learned-selectors" payload shell (template: `WebReaper/ConfigStorage/Concrete/ScraperConfigStore.cs`). No new persistence; validator is the existing fold. Covered by ADR-0012; the shell is `System.Text.Json` from day one. Free OSS.

### 2.3 Source-gen typed extraction `[ScrapeSchema]` — fits the fold seam; new package

`[ScrapeSchema]` POCOs + a C# source generator emitting (a) a `Schema`/`SchemaElement` tree (`WebReaper/Domain/Parsing/Schema.cs`) and (b) a reflection-free typed materializer + `System.Text.Json` `JsonSerializerContext`. Additive: generated `Schema` feeds the existing fold unchanged; the typed projection sits over the **now-STJ** return (the `JObject` return is already gone — ADR-0008, shipped 7.0.0). The generator maps CLR types onto the fold's existing `DataType` coercion grammar (`SchemaContentParser.Coerce`) — reused, not re-derived (ADR-0002). New `WebReaper.Extraction.Generators` analyzer package, **free OSS — this is the funnel's signature differentiator**. **ADR-0010**; the §2.12 prerequisite is **already met (7.0.0)**.

### 2.4 AI substrate — new seam in new package, bound to `Microsoft.Extensions.AI`

A new free `WebReaper.AI` package: the LLM extraction backend the router (2.1) invokes on fallback, bound to **`Microsoft.Extensions.AI.Abstractions` (`IChatClient`/`IEmbeddingGenerator`)** — the durable GA layer — explicitly not Semantic Kernel/Agent Framework naming. Model backends pluggable (frontier, small/self-hosted). Same single-deep-interface shape as ADR-0004's `IPageLoadTransport`. Covered by ADR-0012 (the router); the binding choice warrants **ADR-0013**. Free OSS, BYO keys.

### 2.5 Agent surface — **CLI is the primitive; Skill and MCP are adapters over it**

May-2026 evidence (≈ 35× MCP-vs-CLI token overhead; progressive-disclosure skills beating large tool-schema payloads; this repo's own deferred-MCP-tool mechanism) makes the primitive a **CLI**, not an MCP server.

- **`WebReaper.Cli` (new project, free OSS, AOT single-binary).** `webreaper scrape|crawl|extract --schema|interact`, thin facade over the existing builders (`ScraperEngineBuilder` → `SpiderBuilder` → `ConfigBuilder`) and `ScraperEngine.RunAsync` (`WebReaper/Core/ScraperEngine.cs:38`). LLM-ready Markdown / typed JSON output. The headline AOT artifact and the most token-efficient agent surface — and the API's on-ramp (a `--remote` flag routes through the hosted service).
- **Agent Skill (`SKILL.md` + bundled CLI calls), free OSS, published.** The primary agent-integration surface: progressive-disclosure, harness-neutral, version-controlled. A funnel asset.
- **MCP server (`WebReaper.Mcp` / `.AspNetCore`), free OSS, *interop adapter*.** Kept for managed/cross-client interop (Cursor/ChatGPT/Copilot Studio); thin facade over the CLI/builders. Official `modelcontextprotocol/csharp-sdk` (≈ 0.9.0-preview); pre-1.0 churn contained to this one non-load-bearing adapter.

**ADR-0014** — *the CLI is the primitive agent surface; Skill and MCP are thin adapters over it.* Builders/engine unchanged; the CLI is a new consumer, not a seam change.

### 2.6 Unblocker transports — fits ADR-0004; free seam + the service's backend

Bright Data/Zyte/Scrapfly/ZenRows/Oxylabs each a new `IPageLoadTransport` implementation (`WebReaper/Core/Loaders/Abstract/IPageLoadTransport.cs`), siblings of `HttpPageLoadTransport`/`BrowserPageLoadTransport`. ADR-0004 used exactly as designed. **In the free library these are a BYO seam** (the user supplies their own provider credentials). A `WebReaperApiTransport` adapter — "route this load through the hosted WebReaper API" — is the **conversion bridge** from funnel to product. Dispatch gains a transport-selection policy as a constructor collaborator of `PageLoader` (`WebReaper/Core/Loaders/Concrete/PageLoader.cs:30`) — not a new top-level seam (ADR-0005 restraint). **ADR-0015** (transport selection + the API-transport bridge); extends ADR-0004. The *managed* unblocking/proxy backend lives in the **hosted service**, not the package.

### 2.7 Stealth hardening — fits existing assets

PuppeteerExtraSharp `StealthPlugin` is wired only on the proxy branch of `BrowserPageLoadTransport.LaunchAsync` (`:131`); the no-proxy launch (`:119`) gets none. Hardening = apply stealth unconditionally, suppress the `Runtime.enable` CDP tripwire, canonicalize args, fix the stale Chrome-106 UA (`HttpPageLoadTransport.cs:28`). Bug-fix-by-construction; no new seam; free OSS (basic). Folded into ADR-0015 as "deliberate consequences." Aggressive/maintained stealth is a service concern (the treadmill the evidence says you sell, not ship).

### 2.8 RAG / Markdown normalization — new module

Clean Markdown, accessibility-tree snapshots, screenshots, metadata as reusable artifacts: a transform between the loaded document and `ParsedData`/sinks — not a fold concern (ADR-0002) nor a Row format (ADR-0006). Consumed by the CLI/MCP (2.5) and a new sink. Extending `ParsedData` (`WebReaper/Sinks/Models/ParsedData.cs`, now `record ParsedData(string Url, JsonObject Data)` — STJ, ADR-0008 shipped) touches the ADR-0002/0003 payload surface; the §2.12 prerequisite is **already met (7.0.0)**. **ADR-0016**. Free OSS — RAG-ready output is funnel table-stakes.

### 2.9 Budget governor — composed into the router

Token + browser budgets, route-by-cost, escalate-only-on-validation-failure. A policy collaborator of `IExtractionRouter` (2.1) and the transport-selection policy (2.6), not a standalone seam. Covered by ADR-0012. Free OSS. (In the hosted service it also meters billable usage.)

### 2.10 Observability / replay — **new seam (justified)**

DOM snapshots, HAR, trace/replay, extraction confidence, validation diffs — the research's #1 debugging pain. Real variation (no-op, local-file, hosted) → clears the ADR-0002 bar. New `IExtractionTrace` seam, **no-op + local-file adapters free in the package**; the **hosted dashboard/replay UI is a paid surface of the service**, behind the same seam. Injected at the Spider I/O shell (`WebReaper/Core/Spider/Concrete/Spider.cs`, which already owns `PostProcessor`/`ScrapedData` at `:99–101`). **ADR-0018**.

### 2.11 Compliance / governance — **new seam (justified)**

robots.txt/Content-Signals, crawl-purpose declaration, rate/retry policy, PII masking, audit logs. Real variation, first-class product objects. New `ICompliancePolicy` seam, **free OSS**, evaluated in the Spider shell before `PageLoader.LoadAsync` (`Spider.cs:62`, reusing the `UrlBlackList` site `Spider.cs:56` as precedent); PII masking is a pre-sink transform (`Spider.cs:114`). **ADR-0019**. Managed audit retention can be a service add-on later; the policy engine itself ships free (it makes the funnel trustworthy).

### 2.12 AOT / System.Text.Json migration — ✅ **DONE in 7.0.0 (ADR-0008)** — *retired from the roadmap*

> **Status (Rev 4):** This entire workstream **shipped** as ADR-0008 across the 6.0.0→7.0.0 line. Verified against source: the content-parser seam is now `IJsonContentParser.ParseToJsonAsync` returning `System.Text.Json.Nodes.JsonObject`; `ParsedData` is `record ParsedData(string Url, JsonObject Data)`; core is fully Newtonsoft-free (the `Newtonsoft.Json` PackageReference is dropped, `TypeNameHandling.Auto` replaced by STJ source-gen) and `IsAotCompatible=true` whole-core, guarded by the in-solution `WebReaper.AotSmokeTest`. It landed **cleaner than the staged plan below**: the `JObject` shim was *removed outright* at 6.0.0 (not parked `[Obsolete]`) and core became fully Newtonsoft-free at 7.0.0 — there is no remaining `JObject` deprecation tail. Nothing here is a Phase-0/1 task anymore; §2.12 is now the *precondition the rest of this plan builds on*. The original pre-7.0.0 analysis is retained verbatim below for the reasoning trail only.

*Original pre-7.0.0 analysis (historical):* The load-bearing technical risk. Verified blast radius (21 core files use Newtonsoft): `IContentParser.ParseAsync` returns `JObject` (`WebReaper/Core/Parser/Abstract/IContentParser.cs:8`); `ParsedData` carries `JObject` (`:5`); `IFileSinkFormat.Header/FormatRow` take `JObject`; `PostProcess(Func<Metadata, JObject, Task>)` public on both builders; `ScraperConfigStore` + `AzureServiceBusScheduler` use `TypeNameHandling.Auto` (AOT-hostile; needed for polymorphic `PageAction.Parameters`/`ImmutableQueue<LinkPathSelector>`); the fold builds `JObject/JArray/JToken`. **Good news (verified):** no reflection/`Activator`/`dynamic` anywhere — the only structural AOT blocker is Newtonsoft + `TypeNameHandling`. No `PublishAot` set today.

**Staged (typed path alongside `JObject`, deprecate later — never big-bang):** (1) add a typed parallel return via the source generator (2.3), keep the `JObject` path `[Obsolete]`; (2) replace `TypeNameHandling.Auto` with STJ source-gen `JsonSerializerContext` + `[JsonPolymorphic]`/`[JsonDerivedType]` (supersedes the *serialization mechanism* of ADR-0003, preserves its structural result; closes the open ADR-0005 RedisScheduler Job round-trip as a deliberate consequence); (3) migrate sinks/formats to the typed model (ADR-0006 drain untouched); (4) set `PublishAot`/`InvariantGlobalization`, AOT smoke test, deprecate the `JObject` path. **ADR-0008**; **major SemVer bump** (precedent ADR-0004). This makes the funnel credible *and* gives the service a fast, cheap, single-binary core — no-regret either way.

---

## 3. Free funnel vs paid service boundary

The open-core/`Pro.*` table is **retired** (Rev 3). The boundary is now funnel vs. operated service.

| Surface | Tier | Rationale (evidence) |
|---|---|---|
| `WebReaper` core, `WebReaper.Cli`, `WebReaper.Extraction.Generators`, `WebReaper.AI`, `WebReaper.Mcp`, `WebReaper.Normalization`, Agent Skill, all seams (incl. unblocker seam, `IExtractionTrace` no-op/local, `ICompliancePolicy`) | **Free, MIT** | The funnel. ZZZ-Projects base rate: a popular free library is the SEO/credibility engine, monetised via a *separate* product — crippling it kills the funnel. No copyleft (deters embedding/adoption). |
| **WebReaper API** — hosted, usage-priced unblocking/extraction endpoint: managed proxy rotation/unblocking, JS rendering at scale, retries/reliability, maintained anti-bot, optional hosted observability/replay & audit retention | **Paid service** | ScrapingBee/ScraperAPI/Zyte base rate: customers pay for the *billable operation*, not shipped code. The painful, recurring, ops-heavy part is the only thing with proven solo scraping revenue. |
| Technical content engine (deep tutorials, docs, a scraping guide/book, SEO) | **Free, the growth lever** | ScrapingBee's actual acquisition channel (co-founder authored a scraping book); ZZZ Projects' learn* sites. Content → audience → API conversion. |

**Conversion bridge:** the free `WebReaperApiTransport` (`IPageLoadTransport`, §2.6) and `webreaper --remote` — a one-line switch from self-run to the managed API.

**Relicensing (GPL-3.0 → MIT) — reframed & de-risked.** Still required: GPL is a poor funnel license (deters commercial embedding → kills adoption). But Rev 3 *dissolves the hard part*: contributor code is **not** being relicensed into a closed commercial tier — it stays permissive OSS, only *more* permissive. **Contributor audit — run 2026-05-18 (`git shortlog -sne --all`), folded in:** the bulk (≈ 641 commits) is the owner under three of his own identities (`Alex Pavlov`/`Alexander Pavlov` `<alexppavlov93@gmail.com>`, `Alex <business@highcraft.io>`) — not a consent question. Consent-relevant identities, by materiality:

- **`olpavlov@deloitte.com` — 43 commits** (38 + 5 under a quoted-email variant). This is **the only load-bearing one** and is almost certainly the owner under an old employer email — so it is **not a clean-room case**; it needs a written self-attestation *plus* an employer IP-assignment / work-for-hire check (Deloitte). Rev 1–3 understated this as one of four equal "external identities"; it is 43 commits and the real relicense risk.
- **`mike <mmccabe1993@gmail.com>` — 4 commits**; **`Justyn Hunter`** (`jhunter@gsandf.com` + `justynhunter@gmail.com`, one person) — **2 commits**. Genuinely external but tiny and well-seamed → trivially clean-roomable if consent is unreachable.
- **`fossabot <badges@fossa.io>` — 1 commit**, automated badge bot, n/a.

Then written GPL→MIT consent for `mike`/`Justyn` (or clean-room), the employer check for the `@deloitte.com` identity, `CONTRIBUTING.md` + DCO going forward, then flip `LICENSE.txt`/`.csproj`/README. **ADR-0017** (lower stakes than Rev 2: more-permissive, all-OSS, no commercial-tier optics).

---

## 4. Phased roadmap (funnel → API)

Sequenced so the **free funnel is excellent before the API exists**, with a market-discovery gate before any service build.

### Phase 0 — De-risk & validate (hard gate)
**Workstreams:** ~~AOT/STJ blast-radius spike~~ — **done in 7.0.0 (ADR-0008); struck from Phase 0**; MIT-relicense **consent outreach + DCO draft** (the contributor *audit* is already run — §3); CLI+Skill spike (token-cheap agent invocation); **market discovery — the load-bearing gate**: 10–15 structured interviews with .NET devs/data teams who scrape, on willingness to pay for a .NET-credible hosted unblocking/extraction API vs ScrapingBee/ScraperAPI/Zyte/ZenRows, and price sensitivity.
**ADRs:** ADR-0008 ✅ (shipped 7.0.0 — no longer a Phase-0 deliverable); ADR-0017 (relicense — draft).
**Exit:** the AOT/STJ half is **already satisfied by 7.0.0** (whole-core `IsAotCompatible`, `WebReaper.AotSmokeTest` in-solution) — Phase 0's remaining exit is purely the business/legal gate: a validated paid-API value prop + pricing hypothesis + ≥ a handful of "yes I'd pay/try" signals, **AND** relicense contributor consent obtained or clean-room scoped (§3). If the funnel→API thesis fails here, stop before building the service (cheap failure).

### Phase 1 — The funnel, excellent & unencumbered (Pillar: credibility)
**Workstreams:** ~~execute §2.12 (typed path, STJ, sinks, `PublishAot`)~~ — **shipped in 7.0.0 (ADR-0008)**; **relicense MIT**; publish the AOT benchmark (binary-size/memory/single-file framing); ship the library/CLI as the best free .NET extraction stack; start the technical-content engine.
**ADRs:** ADR-0008 already final (shipped 7.0.0); ADR-0017 (relicense) effective.
**Exit:** core AOT zero-warning **already true (7.0.0)**; remaining — benchmark published; package MIT on NuGet; first deep technical content live. (The "`JObject` path `[Obsolete]`" exit is **void** — the shim was removed outright at 6.0.0, not deprecated.)

### Phase 2 — Funnel reach (Pillar: audience)
**Workstreams:** `WebReaper.Extraction.Generators` (`[ScrapeSchema]`); `WebReaper.Cli` GA + published Agent Skill; `WebReaper.Mcp` interop adapter; RAG/Markdown normalization; sustained content/SEO flywheel (tutorials, docs, scraping guide) — the ScrapingBee lever.
**ADRs:** ADR-0010/0012/0013/0014/0016.
**Exit:** `[ScrapeSchema]` typed extraction reflection-free under AOT; CLI single-binary + skill published & adopted; measurable funnel traffic/downloads growth (funnel health, *not* revenue).

### Phase 3 — Hosted API MVP = first revenue (Pillar: product)
**Workstreams:** stand up the **WebReaper API** (managed unblocking/extraction, usage-priced per successful request) on the same engine; first-class .NET SDK + `webreaper --remote`; the free `WebReaperApiTransport` conversion bridge (ADR-0015); metering/billing; lean single-region ops.
**ADRs:** ADR-0015; ADR-0021 ("the hosted service is a separate product over the same engine; the library never depends on it").
**Exit:** first paying customers via the free→API bridge; first measurable MRR; gross-margin-per-request positive.

### Phase 4 — Make the API reliable & sticky (Pillar: durable revenue)
**Workstreams:** managed proxy/unblocker backends (wrap providers — not resell), JS rendering at scale, retries/SLA, maintained anti-bot (the treadmill, now paid), hosted observability/replay dashboard (paid surface behind the free `IExtractionTrace` seam, ADR-0018), self-healing in the service path; grow from solo→tiny team funded by MRR.
**ADRs:** ADR-0018, ADR-0019.
**Exit:** MRR ramp tracking the ScrapingBee reference shape (toward ~$8K→$1M ARR); retention/expansion positive.

### Phase 5 — Deepen the moat (optional / later)
**Workstreams:** TLS/HTTP-2 fingerprint impersonation as a **service capability** (the verified .NET `SocketsHttpHandler` gap), not a NuGet; advanced AI extraction & eval; compliance/audit-retention add-ons; assess exitability (ScrapingBee/ScraperAPI both exited).
**ADRs:** ADR-0020 (TLS impersonation as a scoped service capability).
**Exit:** impersonation passes a JA4/HTTP-2 consistency check, offered as a service tier.

---

## 5. ADRs to author (continuing from 0009)

> **Already authored & shipped in 7.0.0 — do NOT re-author (lineage only):**
> - **ADR-0008** — System.Text.Json source-gen typed pipeline; supersedes the `TypeNameHandling.Auto` serialization grammar of ADR-0002/0003, structural seams preserved. Major SemVer. *(`docs/adr/0008-system-text-json-typed-pipeline.md`)*
> - **ADR-0009** — the builder is a public registration seam; heavy adapters are per-technology satellite packages (`WebReaper.{Cosmos,Mongo,Redis,AzureServiceBus,Puppeteer}`); dependency-light core. *(`docs/adr/0009-registration-seam-and-satellite-adapters.md`)*
>
> **Externally reserved — NOT a repositioning-plan ADR (do not claim this number):**
> - **ADR-0011** — held by an independent, in-progress, **plan-external** ADR (a file-persistence decision in `docs/adr/`, actively evolving and unrelated to this repositioning plan). The repositioning plan must never claim 0011. It sits at 0011 because ADR-0008's prose reserves **0010** for the `[ScrapeSchema]` source generator below, so the next unrelated ADR took the next free number.
>
> The genuinely *to-author* ledger (source-correct: ADR-0008's prose reserves **0010** for `[ScrapeSchema]` and **0017** for the relicense; **0011** is the external reservation above; the remaining items fill **0012–0016 / 0018–0021**):

1. **ADR-0010** — `[ScrapeSchema]` source generator emits the `Schema` tree + a reflection-free materializer; reuses the fold's coercion grammar. *(the number ADR-0008's prose reserves for it)*
2. **ADR-0012** — Deterministic-first → LLM-fallback is an `IExtractionRouter` composed inside the content-parser (`IJsonContentParser`) position; budget governor + self-healing-demotion are its collaborators, not new seams. Free OSS.
3. **ADR-0013** — AI substrate binds to `Microsoft.Extensions.AI` (`IChatClient`), not Semantic Kernel/Agent Framework naming; backends pluggable.
4. **ADR-0014** — The CLI is the primitive agent surface; Skill and MCP are thin adapters; MCP is interop, not the wedge.
5. **ADR-0015** — Unblocker integrations are `IPageLoadTransport` adapters (ADR-0004 extended); a `WebReaperApiTransport` is the free→paid conversion bridge; managed unblocking lives in the service, not the package.
6. **ADR-0016** — RAG-ready Markdown/aria/screenshot artifacts are a normalization module, not a fold (ADR-0002) or Row-format (ADR-0006) concern.
7. **ADR-0017** — Relicense GPL-3.0 → MIT (to make the funnel unencumbered; all code stays permissive OSS, no commercial-tier relicensing); contributor consent + clean-room fallback. *(the number ADR-0008's prose reserves for the relicense)*
8. **ADR-0018** — Trace/replay is a new `IExtractionTrace` seam; no-op/local free in the package, hosted dashboard a paid service surface behind the same seam.
9. **ADR-0019** — Compliance/identity are first-class `ICompliancePolicy` objects at the Spider pre-load site; PII masking is a pre-sink transform. Ships free.
10. **ADR-0020** — TLS/HTTP-2 fingerprint impersonation is a deliberately deferred capability of the *hosted service*, scope/risk/abuse-positioning bounded; not shipped library code.
11. **ADR-0021** *(new)* — The hosted WebReaper API is a separate product built on the same engine; the OSS packages never take a dependency on it; the only coupling is the optional `WebReaperApiTransport` adapter.

---

## 6. Risks & mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| **Funnel → API conversion fails** (free users never pay) | Medium | High | Phase-0 market-discovery hard gate (validate willingness-to-pay before building the service); content-led acquisition like ScrapingBee; the `--remote`/`WebReaperApiTransport` one-line bridge minimizes conversion friction. |
| **Running SLA hosted infra while ~solo** (#1 structural risk per evidence) | High | High | Start narrow (single region, usage caps, wrap a provider backend rather than build proxies); grow solo→tiny team funded by MRR exactly as ScrapingBee/ScraperAPI did; do not scale ops ahead of revenue. |
| API price competition vs entrenched incumbents (ScrapingBee/ScraperAPI/Zyte/ZenRows/Bright Data) | High | Medium | Don't compete on raw proxy price; differentiate on the .NET-native funnel (typed/AOT/CLI/skill/agent DX) + content moat; target the underserved .NET segment incumbents ignore. |
| Newtonsoft→STJ/AOT blast radius | High | High | Staged typed-path-alongside migration (§2.12); Phase-0 spike de-risks polymorphic config first; verified no other reflection bounds the problem; major SemVer accepted (precedent ADR-0004). |
| AOT cold-start number is workload-mismatched (serverless, not the buyer's long-lived workers) | Medium | Low | Reframe the benchmark as binary-size/memory/single-file/no-runtime-install for on-prem; never lead the funnel with a serverless cold-start claim. |
| Anti-bot maintenance treadmill / abuse-risk positioning | High | Medium | The treadmill is *the paid service's job* (evidence: that's what sells), not shipped code; governed/compliance-first (ADR-0019); never market evasion. |
| MCP C# SDK pre-1.0 churn | Medium | Low | Not load-bearing — one non-load-bearing interop adapter (ADR-0014); pin SDK, thin facade. |
| Relicense GPL→MIT consent | Low | Medium | De-risked in Rev 3 (more-permissive, all-OSS, no commercial relicensing of contributor code); audit + consent + per-seam clean-room fallback (ADR-0017). |
| Legal/compliance exposure (EU/EDPB, CNIL, Cloudflare default-deny) | Medium | High | Compliance a first-class free seam (ADR-0019); design for live-retrieval/RAG + future signed-crawler lane, not bulk training; the API can enforce policy centrally. |
| The AbotX trap (monetising the library) | — | — | **Mitigated by the pivot itself** — Rev 3 explicitly does not monetise the library or ship a Pro tier. |

---

## 7. Success metrics & milestones

ScrapingBee/ScraperAPI-shaped, bootstrapped; **stars/downloads are funnel health, explicitly not revenue.**

| Milestone | Metric | Phase |
|---|---|---|
| Thesis validated | Phase-0 market-discovery: validated paid value prop + pricing hypothesis + concrete willingness-to-pay signals | 0 |
| Funnel credible | AOT zero-warning + published benchmark; package MIT; first deep technical content live | 1 |
| Funnel reach | `[ScrapeSchema]`/CLI/skill GA; growing downloads + content traffic (funnel health) | 2 |
| First revenue | Hosted API live; first paying customers via the free→API bridge; first MRR; positive gross-margin-per-request | 3 |
| Durable revenue | MRR ramp on the ScrapingBee reference shape (~$1K→$8K→$1M ARR over ~15 mo as the *reference*, not a promise); positive retention/expansion | 4 |
| Optional outcome | Sustainable lean team funded by MRR; exitability assessed (both proven comparables exited) | 5 |

---

## 8. Explicit non-goals

- **No monetised library / no `WebReaper.Pro.*` tier** — the AbotX precedent; Rev 3's central correction.
- **No donations/sponsorship as income** — proven to fail (QuestPDF ~3%; FusionCache 72M downloads, $0).
- **No residential-proxy business** — the hosted API *wraps* providers as its backend; never resell IPs.
- **No no-code/visual product** — import.io/Octoparse/ParseHub revenue ceiling.
- **No VC-scale assumption** — bootstrapped, content-led, ScrapingBee-shaped; not venture-scale.
- **No Python feature-for-feature parity; no star-count yardstick.**
- **No detection-evasion-for-malicious positioning** — governed, compliance-first; the service maintains stealth, never markets abuse.
- **No bulk-training-corpus focus** — live-retrieval/RAG; support a future signed-crawler/verified-bot lane.
- **No browser-fork stealth engine in-house** — integrate the OSS frontier via remote CDP.
- **No new `CrawlOutcome` arm / no per-step strategy layer** — ADR-0001 stands; routing is composed in the `IContentParser` position.
- **No `RegEx` selector backend** — ADR-0007 settled this.
- **No big-bang rewrite** — every change evolves an existing ADR-justified seam; the STJ/AOT break is isolated and SemVer-flagged.
- **No OSS dependency on the hosted service** — one-way only, via the optional `WebReaperApiTransport` (ADR-0021).

---

## Critical files for implementation

- `WebReaper/Core/Parser/Abstract/IJsonContentParser.cs` — the content-parser seam; its `JObject` return was **already replaced** by `Task<JsonObject> ParseToJsonAsync(...)` (`System.Text.Json`, ADR-0008, shipped 7.0.0). The router (ADR-0012) composes at this now-STJ position.
- `WebReaper/Core/Parser/Concrete/SchemaContentParser.cs` — the single Schema fold (ADR-0002); its terminal is **already STJ** (ADR-0008, shipped); still the `[ScrapeSchema]` source generator's coercion-grammar source (ADR-0010).
- `WebReaper/Core/Loaders/Abstract/IPageLoadTransport.cs` — the ADR-0004 seam where unblocker adapters and the `WebReaperApiTransport` conversion bridge (ADR-0015) plug in; the deferred TLS service capability (ADR-0020) is server-side.
- `WebReaper/Core/Spider/Concrete/Spider.cs` — the I/O shell; injection point for `IExtractionTrace` (ADR-0018) and `ICompliancePolicy` (ADR-0019); `UrlBlackList`/sink-fan-out sites are precedents.
- `WebReaper/ConfigStorage/Concrete/ScraperConfigStore.cs` — the payload shell (ADR-0003); its `TypeNameHandling.Auto` was **already superseded** by STJ source-gen (ADR-0008, shipped 7.0.0); template for the learned-selectors shell (§2.2).
- `WebReaper/Builders/ScraperEngineBuilder.cs` — the public fluent surface every new capability must extend additively.
