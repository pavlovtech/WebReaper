# 0078. Derived Tool Registries and Transport Dispatch Exhaustiveness

- **Status:** Accepted — design pass; implementation pending (2026-05-29)
- **Date:** 2026-05-29
- **Deciders:** Alex (HITL), Claude (architecture pass)

## Context

A **Page action** arm's meaning is spread across four packages: the `PageAction`
closed sum (core), its codec (core), the **Page action** builder (core), the two
browser **Load transport** dispatch switches (`WebReaper.Cdp`,
`WebReaper.Playwright`), and the AI satellite's tool projections + registries +
parse switches. ADR-0009 forbids core referencing the satellites, so an arm
cannot literally live in one file.

The ADR-0060 amendment (2026-05-28) already co-located each arm's three AI
concerns — `Name`, `Descriptor`, `FromArguments` — into one nested static class
per arm (`PageActionTools` / `AgentDecisionTools`). That scatter is gone. Two
problems remain:

1. **Four parallel hand-synced lists** in the satellite: `ForBrain()` (13
   descriptors), `ForResolver()` (9 descriptors), `ParseDecisionTool` (13-arm
   switch in `LlmAgentBrain`), `ParseActionTool` (9-arm switch in
   `LlmActionResolver`). The descriptor list and the parse switch must agree by
   hand. The failure is **silent**: register an arm in `ForBrain()` but forget
   `ParseDecisionTool`, and the model calls a tool it is offered, the parse
   falls through `_ => Stop`, and the model's choice silently becomes
   termination — no error, no log. (`ParseActionTool` falls through `_ =>
   null`.)
2. **The two transport dispatch switches** are switch-*statements* with a
   runtime `default: throw ArgumentOutOfRangeException`. Adding a `PageAction`
   arm compiles cleanly with the arm silently unhandled until a page exercises
   it.

## Decision

Two axes.

**Axis A — exhaustiveness idiom on the transports.** Convert both transport
dispatch switch-statements (`CdpPageActionDispatcher.PerformAsync`,
`PlaywrightPageLoadTransport.PerformAsync`) to switch-*expressions* with no
discard arm. Adding a `PageAction` arm then fails to compile in each satellite
until handled — the runtime `default: throw` becomes a compile-time guarantee.
No new module, no core↔satellite seam; each transport keeps its native body
(CDP's `Runtime.evaluate` JS, Playwright's locator/keyboard APIs). ADR-0009
untouched.

**Axis B — derived registries in the satellite.** Both the brain and resolver
registries become derived views of one `PageActionArms` arm list. Each entry is
a `(Descriptor, Parse)` pair, so the tool offered to the model and the parse
that decodes its call are the same list seen two ways — `Tools = arms.Select(e
=> e.Descriptor)` and the parse is keyed off the same list. The registration
list and the parse dispatch cannot drift. The brain registry is the three
brain-native arms (`Extract` / `Follow` / `Stop`) plus `PageActionArms` mapped
through an `Act`-wrapping adapter; the resolver registry is `PageActionArms`
filtered to entries that expose a resolver adapter.

Fork 8 of ADR-0060 (the resolver must never return `SemanticAct`, which would
loop the transport's resolution path) is preserved **structurally**:
`SemanticAct` exposes no resolver adapter, so it cannot appear in a registry
derived from the arm list — a compile-time absence, not a runtime `.Where(x !=
SemanticAct)` filter or a hand-maintained second list. The `_ => null`
(resolver) / `_ => Stop` (brain) fallback remains, but now catches only a
genuinely hallucinated tool name, never a wiring omission.

## Consequences

Good:
- Adding a `PageAction` arm drops from four satellite edits (two lists + two
  switches) to one (`PageActionArms`) plus at most one (a brain-native arm).
- The silent-drop failure class is eliminated: the descriptor and the parse are
  the same list, so an arm offered to the model is always parseable.
- A new arm that a transport forgets is now a compile error in that satellite.
- Fork 8's loop-prevention stays structural — strengthened from
  double-omission-from-two-lists to single-absence-of-an-adapter.

Bad / costs:
- One derived-registry indirection in the satellite (an arm list + a mapping)
  in place of two literal lists.

## Alternatives considered

- **A shared `IBrowserPrimitives` seam in core, one dispatch parameterized over
  it, each transport implementing the primitives.** Rejected: it forces
  Playwright to drop its native methods (`page.FillAsync`,
  `ScrollIntoViewIfNeededAsync`, `Keyboard.PressAsync`) for generic
  evaluate-JS compositions — a capability regression — and it introduces a
  core↔satellite seam that re-litigates ADR-0009. The twin switches are
  correct-by-design (transport-specific bodies); only their *exhaustiveness
  enforcement* was weak, which Axis A fixes without a new seam.
- **Leave Axis B as-is** (the amendment already co-located per-arm concerns).
  Rejected: the residual four-list sync still allows the silent-drop failure,
  which is invisible (offered to the model, unparseable, no error).
- **Move each arm's full definition into one file across packages.** Impossible
  under ADR-0009 (core cannot reference the satellites); the per-axis approach
  is the most concentration reachable without breaking the quarantine.
