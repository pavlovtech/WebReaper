# Lead Discovery Harness, Build Plan (Level 1)

**Status:** Proposed experiment (a Gate-1 dogfood). NOT a committed product, NOT a change to WebReaper core.
**Date:** 2026-05-29
**Owner:** Alex
**Decision trail:** the 2026-05-29 strategy grill (positioning WebReaper for lead generation). Companion record: [docs/adr/0080](adr/0080-entity-resolution-and-provenance-layer.md) (the heavy entity layer, deliberately deferred).

## One-line goal

A WebReaper-consumer harness that turns a plain-language hypothesis plus named source(s) into a verified, evidenced, partially-enriched company list, to dogfood HighCraft's own outbound and answer Gate 1.

## The two gates this exists to test

- **Gate 1 (does the tool beat the incumbent for me):** on one real hypothesis, the harness produces a list that is fresher, better-verified, and cheaper per qualified company than Claygent/Clay run on the same candidates. Pass, it earns a place in your outbound. Fail, you keep using Clay and you lost days, not a quarter.
- **Gate 2 (is there a product), deferred:** a Ukrainian dev-shop peer runs it unprompted on their own data and would pay. Only relevant if Gate 1 passes.

## Non-goals (explicit, to stop scope creep)

- **Level 2** (hypothesis-only, agent auto-selects sources). Deferred: the open web gives a noisy, partial company universe where databases give a clean one, and a bad auto-list corrupts hypothesis validation. Keep human judgment on "which source."
- **The ADR-0080 entity / identity / provenance layer and entity store.** Deferred to Gate-2 / product-era. Level 1 needs only company dedupe by domain.
- **LinkedIn first-party scraping or bought-account pools.** Off-limits (legal: Proxycurl and ProAPIs both lost in 2025). LinkedIn, if ever needed, comes only through a BYO-key third-party connector (Apify), and it is not needed for v0.
- **A product, a GUI, satellites, a cloud.** All Gate-2 and beyond.
- **Changes to WebReaper core.** Build in a consumer; graduate a piece into core only on Gate-1 evidence.

## Where it lives

A consumer console app, `Misc/WebReaper.LeadDiscovery`, that references WebReaper as a library (Misc/ is a consumer, not packaged, so core and the NuGet surface stay clean). Keep real hypotheses, sources, and company lists OUT of the repo (pass them as args or local gitignored files). If it graduates to a product or starts holding sensitive lists, move it to a private repo.

## Architecture: three stages, reuse first

```
hypothesis + source(s)
        |
  [Stage 1] crawl source  ->  candidate companies  ->  normalize to domains  ->  dedupe
        |
  [Stage 2] per company: AgentEngine over public site/careers/news
                -> { match, confidence, evidence:[urls], why_now, fields:{...} }
        |
  [Stage 3] emit verified rows (CSV/JSONL) + run report (counts, match-rate, cost tally)
```

### Stage 1, source to candidates (new, small)
Crawl each named source with the Crawl driver; extract candidate company names or domains (ExtractInferred, or a simple selector schema for a known source); normalize to a root domain; dedupe by normalized domain.
Known wrinkle: some sources yield company names, not links, so a name-to-domain resolution step is needed. v0 may best-effort guess and flag unresolved rather than block.

### Stage 2, per-company verify and enrich (the node, the core value)
For each candidate domain, run `AgentEngine` with the hypothesis as the goal over the company's public site, careers, and news. The agent returns a structured result: `{ match: bool, confidence: 0..1, evidence: [urls/snippets], why_now: string?, fields: {requested public-signal fields} }`. The `why_now` (just posted a relevant role, just raised) is the high-value field, it is the "why this company, why now."

### Stage 3, output and report
Emit verified rows to CSV/JSONL via existing sinks. Print a run report: candidate count, matched count, match-rate, a simple per-run cost tally (LLM tokens times price, summed; not the ADR-0066 rework), and per-stage timing.

## Reused vs new

- **Reused (WebReaper library):** Crawl driver, AgentEngine / AgentEngineBuilder, ExtractInferred / ISchemaInferrer, IScraperSink (Csv, JsonLines), RunReport telemetry.
- **New, local to the harness, all small:**
  - the batch loop over candidates with a shared report and a **budget cap**; the policy is "process to budget, then mark unreached rows explicitly," so results are not silently order-dependent (the prototype's lesson);
  - domain normalization and dedupe;
  - the `{match, confidence, evidence, why_now}` shape as the agent's structured output (no ISchemaValidator rework; the agent reports its own confidence);
  - flat CSV columns carrying per-field source and evidence (lossy but fine for v0; the full provenance envelope is ADR-0080, deferred).

## Build order (tracer-bullet, highest-risk-first)

1. Scaffold `Misc/WebReaper.LeadDiscovery` referencing WebReaper plus the AI satellite. CLI: `discover --hypothesis "..." --source <url> [--source ...] --fields a,b,c --budget 1.00 --format csv|jsonl --out FILE`.
2. **Stage 2 first**, on a hand-fed list of 3 domains. Prove the per-company verify works and the `{match, confidence, evidence, why_now, fields}` output is legible. (Core value and the riskiest piece, the LLM output shape, gets de-risked before any crawling.)
3. **Stage 1** on one real source; wire its candidates into Stage 2.
4. **Stage 3** report, cost tally, and the budget/ordering policy.
5. **Run the Gate-1 test** on the chosen hypothesis and source against Claygent on the same candidate list; record the result.

## Gate-1 test protocol (the actual point)

When ready, pick: ONE real hypothesis (ideally health-tech or EMR, on your north star), ONE source the hypothesis lives on, and run Claygent/Clay on the SAME candidate list. Judge on the same N companies:
- **fresher:** are the `why_now` signals current,
- **better-verified:** higher precision on a manual spot-check of ~20 rows (fewer false matches),
- **cheaper:** lower cost per qualified company.

Record the numbers. The decision is data, not vibes.

## Graduation rule

Fold a harness piece into WebReaper core or a satellite ONLY if Gate 1 passes AND the piece is general. The batch-discovery loop could become a real third driver (ADR-worthy then); a proven provenance need would re-open ADR-0080. Until that evidence exists, everything stays in the consumer.
