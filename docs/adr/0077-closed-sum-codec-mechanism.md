# 0077. Closed-Sum Codec Mechanism

- **Status:** Accepted — design pass; implementation pending (2026-05-29)
- **Date:** 2026-05-29
- **Deciders:** Alex (HITL), Claude (architecture pass)

## Context

The flat **closed sum**s persist to JSON through hand-written codecs in
`WebReaper/Serialization/Converters/`: `PageActionCodec`, `AgentDecisionCodec`,
`AgentDecisionOutcomeCodec`. They are near-identical boilerplate — the doc
comments themselves say "same shape as `PageActionCodec`". Each is: `Write`
opens an object, writes a `type` tag, switches per arm to write fields, closes;
`Read` hoists a nullable local per possible field, loops `while (r.Read())`
draining properties, then a final `type switch` constructs the arm via a
copy-pasted `Require` helper (three verbatim copies). Adding one arm (ADR-0074
added three) is three edits per codec: the write switch, the read property
loop, and the read type switch.

ADR-0008 mandates hand-written, reflection-free codecs for AOT safety; that
posture is not in question. What is in question is why three codecs re-implement
the *same* hand-written machine. The `WebReaper.AI` satellite already solved the
analogous problem on the LLM side: one deep **LLM call** mechanism (`LlmCall<T>`)
that the five `Llm*` adapters describe via thin `LlmCallDescriptor<T>` records.
The serialization layer never got that treatment.

The load-bearing constraint on any shared mechanism: `Utf8JsonReader` is a `ref
struct` — it cannot be captured in a delegate, stored, or crossed over `await`
— so the read side cannot hand per-arm read logic to a mechanism as a `Func<>`
the way `LlmCall<T>` takes `ParseResponse`. This is precisely why each codec
hand-streams its own read loop today.

## Decision

We will add a `ClosedSumCodec<T>` mechanism plus a per-arm descriptor — the
serialization-layer sibling of `LlmCall<T>` + `LlmCallDescriptor<T>`. The
mechanism owns the object envelope, the `type` discriminator (write + dispatch),
the **one-time `JsonNode.Parse(ref reader)` materialization** (the ref-struct
reader is consumed once at the top of `Read`, then per-arm build delegates run
on a plain `JsonObject`), the single `Require` missing-field contract, the
unknown-tag throw, and the common-field pass. Each arm's descriptor owns only
its tag, its field writes, and its build-from-`JsonObject`.

Interface shape (chosen after designing it three ways — see Alternatives): the
**minimal spine** — `Arm<TArm>(tag, write: (writer, x) => …, build: (obj, ctx)
=> …)` with an `ArmReaderContext` ref struct owning `Require` / `RequireChild` /
`Optional*` / `Common` — plus a **field-less one-liner overload**
(`Arm<TArm>(tag, Func<TArm> create)`) for `ScrollToEnd` / `WaitForNetworkIdle`
/ `None` / `Stop`. The common `reason` field on `AgentDecision` is consumed
inside each arm's `build` via `ctx.Common("reason")`, so arms stay immutable and
construct once.

Scope (full-flat): migrate the three flat tag-sums — `PageAction`,
`AgentDecisionOutcome`, `AgentDecision`. `Schema` gains a `From(node)` entry so
a migrated `AgentDecision.Extract` can read it, but **stays bespoke** (it is a
recursive container/leaf tree discriminated on `$kind`, not a flat tag-sum);
the mechanism composes with it, it is not built by it. `AgentRunSnapshot` (a
product type with arrays) and the `ImmutableQueue` selector-chain / backlink
converters (collections) also stay bespoke. Every existing streaming caller
(`AgentRunSnapshotCodec`, the selector-chain converter, the source-gen
`List<PageAction>` wrapper) keeps calling the migrated codecs' streaming
`Read(ref r)` unchanged — the mechanism exposes that entrypoint, implemented as
`From(JsonNode.Parse(ref r))`, so migration is incremental, not all-or-nothing.

Drift between the write and read halves is guarded by adjacency (the `write:` /
`build:` pair per arm) plus a per-sum round-trip test, not by deriving both from
one declaration.

## Consequences

Good:
- Adding an arm becomes one descriptor row instead of three edits across a
  write switch, a read loop, and a read type switch.
- The `Require` contract, the unknown-tag throw, and the materialize-once
  discipline live in one place; the three duplicated `Require` helpers collapse
  to one.
- One consistent "mechanism + descriptor" idiom across the codebase (LLM calls
  and closed-sum serialization).
- AOT held — `System.Text.Json.Nodes` only, no reflection (ADR-0008 stands).

Bad / costs:
- The read side allocates one `JsonObject` per value (materialize-then-dispatch).
  Irrelevant at these call frequencies (config load, agent resume, per **Agent
  step**), and the outcome/snapshot codecs already pay it for their nested
  payloads; the pure-scalar `PageAction` path is the one that goes from
  zero-alloc to one-object-per-arm.
- The common-field / init-only-record wrinkle is handled via `ctx.Common`, one
  extra clause per arm on the read side of `AgentDecision` only.

## Alternatives considered

- **Design 2 (maximally flexible):** first-class field-codec / custom-field /
  overridable-discriminator seams. Rejected: that flexibility was sized for
  `Schema`-like oddballs, which we are deliberately keeping bespoke; the three
  target sums are all flat `type`-tag shaped.
- **Design 3 (typed field-chain, one line per flat arm, auto-keyed JSON
  names).** Rejected: auto-keying the wire name from the property name is a
  silent wire-compat footgun on *persisted* agent snapshots and configs (the
  `ms` / `timeoutMs` divergence), it regresses every non-flat arm into raw
  `ArmCustom`, and it adds arity-overload scaffolding plus a `Reason = ""`
  placeholder wart for init-only records — mechanism complexity to save
  characters in the descriptor.
- **Asymmetric (shared Write mechanism only, leave Read hand-streamed).**
  Rejected: the read loop is the larger, more error-prone duplicated half.
- **Source generation.** Rejected: ADR-0008's no-reflection / no-codegen
  posture, and these are not hot paths — a hand-written shared mechanism is the
  cheaper, AOT-trivial choice.
