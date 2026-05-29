# 0080. Entity resolution and provenance layer

**Status:** Deferred (shelved 2026-05-29). Designed as a pass, not built. Revisit only if the Lead Discovery Harness clears Gate 1 and a real identity or provenance need is proven.
**Date:** 2026-05-29
**Deciders:** Alex

## Context

Exploring whether to extend WebReaper toward lead generation (hypothesis-driven discovery plus enrichment, a Clay-shaped use case) raised the question of a canonical entity plus identity-resolution plus per-field provenance layer: the substrate a multi-source enrichment product would need to merge the same company or person across sources and to attribute every field to where it came from.

A design pass was done (preserved below). A grilling session against the project's goals and the owner's capacity concluded that building this now is premature. The first step is a thin Level 1 discovery harness built as a WebReaper consumer (see [docs/LEAD-DISCOVERY-HARNESS-PLAN.md](../LEAD-DISCOVERY-HARNESS-PLAN.md)), which deliberately does NOT need this layer: a single list, a single pass, company dedupe by normalized domain only.

## Decision

Defer. Do not build the entity layer now. The Level 1 harness is the committed path; it stays in a consumer project and uses domain-level dedupe only. This ADR is kept as a record so the design and its open questions survive for revival.

## Why deferred (the grill's findings)

- Misaligned with the owner's written north star (HighCraft as a healthcare-services leader, not martech), and it fails his own goal filters (at most 4 goals per year; unit economics that close in 6 to 9 months).
- No spare capacity (single-client revenue concentration on iCareDx; roughly 232 tracked hours per month on delivery).
- "AI makes building cheap" means the build is not the moat, for anyone. The scarce things (distribution, the why-now reasoning) are not solved by this layer.
- The harness can prove or kill the value with far less. If it clears Gate 1 and a general piece emerges, this ADR re-opens with evidence behind it.

## Proposed design (preserved for revival)

A canonical entity plus identity-resolution layer added as an opt-in terminal stage over the extracted-record stream:

1. **Canonical entity model** (a new `WebReaper.Entities` namespace): `CanonicalEntity` as a typed projection over the untyped `JsonObject`, not a replacement. `Person` and `Company` as an `EntityKind` discriminator plus a known-key field vocabulary over a `Dictionary<string, FieldValue>`, deliberately not fixed-property records (same posture as schema-is-a-tree-not-a-class).
2. **Identity resolution** (`IIdentityResolver`, returning a `Matched | Created` closed sum), decomposing into `IMatchKeyExtractor` (blocking keys to avoid O(n squared)), `IEntityMatchScorer` (confidence in [0,1] with upper and lower thresholds), and `IConflictResolver` (when sources disagree).
3. **Per-field provenance as an envelope**: `FieldValue = { Value, Provenance }`, co-located with the value, where `Provenance` carries `SourceId`, `Confidence`, `ObservedAt`, `Status (Active | Superseded)`, and superseded `History`.
4. **Placement** as a stateful `EntityResolutionSink` (an `IScraperSink`) downstream of the ADR-0076 PostExtractionPipeline.
5. **Review queue** for the mid-confidence band, persisted as data, not a UI.
6. **`IEntityStore`** shaped like `IAgentRunStore` (InMemory plus File in core; Redis, Mongo, Sqlite, Cosmos as satellite adapters), cross-crawl from v1.

## Open questions to resolve if un-deferred

1. **Sink vs. driver placement.** A stateful `EntityResolutionSink` inverts the fan-out model (sinks are leaves, not sources). Is entity resolution really a first-class entity driver (sibling to the Crawl and Agent drivers), not a sink that secretly is not a leaf? A post-crawl batch pass sidesteps the concurrency hazard but breaks the agent's streaming model.
2. **Seam over-proliferation.** Six new seams, each with one implementation at ship, violates the project's "a seam waits for its second adapter" discipline (ADR-0036, ADR-0079). Apply the deletion test: collapse match-key, scorer, and conflict into one `IIdentityResolver` with internal helpers until a second adapter proves each axis.
3. **EntityId minting under at-least-once delivery.** Is `EntityId` content-derived (deterministic from match keys, so retries converge) or a random surrogate (needing idempotency separate from the per-URL visited-link tracker)? This changes the store contract and the whole resumability story.
4. **Source-trust ontology.** Conflict resolution needs a trust ranking that the library cannot author (it is per-customer, and per-contract for a waterfall). Is trust a number on `Provenance`, config on the resolver, or out of scope (leaving "most-recent-wins," which lets a stale scrape clobber a verified field)?
5. **Provenance vs. legality.** Per-field provenance answers GDPR Art. 14 attribution, the easy half. It does nothing for lawful basis, and the never-discard `History` is in direct tension with erasure. Does a "scrape people into a lead database" primitive belong in the MIT core at all, or only behind a paid tier with a contract and a DPA?
6. **The `FieldValue` vs. `JsonObject` boundary.** A customer wants both rows (for a warehouse) and entities (for a CRM). Does the entity layer round-trip to a flattenable `JsonObject` (losing provenance, or breaking every flat-row sink), or is it a hard one-way boundary?

## Alternatives considered

- **Build it now as the foundation of a lead-gen product.** Rejected: see "Why deferred." Premature, misaligned, and not the moat.
- **A post-crawl batch reconciliation pass instead of an inline stage.** Viable, avoids the concurrency hazard, but breaks live re-enrichment and the agent's streaming model. Re-evaluate if un-deferred.

## References

- [docs/LEAD-DISCOVERY-HARNESS-PLAN.md](../LEAD-DISCOVERY-HARNESS-PLAN.md), the lighter path chosen instead.
- ADR-0076 (PostExtractionPipeline), the pipeline stage this would sit downstream of.
- ADR-0046 (ExtractionRouter), which a provider-waterfall would generalize.
- The 2026-05-29 strategy grill (conversation record).
