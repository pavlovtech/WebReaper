# 0078. Derived Tool Registries and Transport Dispatch Exhaustiveness

- **Status:** Accepted — implemented 2026-05-29. Axis A's original
  switch-expression mechanism proved infeasible in C# and was re-decided as a
  CI coverage guard (see Decision + Alternatives).
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

**Axis A — exhaustiveness guard on the transports.** The two transport dispatch
switch-*statements* (`CdpPageActionDispatcher.PerformAsync`,
`PlaywrightPageLoadTransport.PerformAsync`) keep their `default: throw
ArgumentOutOfRangeException`, and a CI test asserts every `PageAction` arm
dispatches without reaching that default. No new module, no core/satellite
seam; each transport keeps its native body (CDP's `Runtime.evaluate` JS,
Playwright's locator/keyboard APIs). ADR-0009 untouched.

This re-decides the design pass's original plan (convert the switch-*statements*
to discard-less switch-*expressions* so a new arm fails to compile). That plan
rests on a compiler capability C# does not have. C# has no closed-hierarchy
exhaustiveness (discriminated unions are unshipped as of C# 14 / .NET 10), so a
discard-less switch expression over `PageAction` reports CS8509 ("not
exhaustive") even when every arm is handled; the closed sum's private
constructor does not change this. CS8509 is therefore present identically
whether or not a new arm has been added, so as a warning it carries no
incremental signal, and promoting it to an error breaks the already-complete
switch today. A discard-less switch expression cannot be both clean now and a
compile error on a new arm. (Verified empirically on the .NET 10 SDK across
`LangVersion` 12 and `latest`.)

The achievable guard is a test, run in CI:
- **CDP** gets an execution-coverage test
  (`WebReaper.Cdp.Tests.CdpPageActionDispatchTests`): it reflects every
  `PageAction` arm, dispatches each through `PerformAsync` against the
  `FakeCdpSession`, and asserts none throws `ArgumentOutOfRangeException`. A new
  arm trips a reflection completeness check (forcing a sample), and the sample
  then proves the CDP transport handles it.
- **Playwright** gets the same execution-coverage test
  (`WebReaper.Playwright.Tests.PlaywrightPageActionDispatchTests`): the dispatch
  was extracted into an internal `PlaywrightPageActionDispatcher` (mirroring the
  CDP extraction) so it is reachable, and `Microsoft.Playwright`'s `IPage` is
  mocked with NSubstitute (a ~100-member interface, too large to hand-stub like
  the small `ICdpSession`; the dep is test-only and isolated to that one
  project). Originally deferred; closed in a follow-up.
- A core-side arm-census test (`WebReaper.UnitTests.PageActionArmCensusTests`)
  remains as the transport-agnostic tripwire: it pins the `PageAction` arm set
  and fails with a checklist of every consumer to update (both transports, the
  AI registries, the builder, the codec) when an arm is added.

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
`SemanticAct`'s arm entry carries no resolver adapter (`ResolverToAction` is
`null`), so the resolver registry and the resolver parse, both derived from
`Arms.Where(a => a.ResolverToAction is not null)`, omit it together. The absence
is structural (the entry has nothing for the derivation to include), not a
runtime identity filter (`.Where(x != SemanticAct)`) or a hand-maintained second
list. The `_ => null` (resolver) / `_ => Stop` (brain) fallback remains, but now
catches only a genuinely hallucinated tool name, never a wiring omission.

## Consequences

Good:
- Adding a `PageAction` arm drops from four satellite edits (two lists + two
  switches) to one (`PageActionArms`) plus at most one (a brain-native arm).
- The silent-drop failure class is eliminated: the descriptor and the parse are
  the same list, so an arm offered to the model is always parseable.
- A new arm a transport forgets is caught in CI: both transports have an
  execution-coverage test (CDP via `FakeCdpSession`, Playwright via an
  NSubstitute `IPage`), and the core arm-census tripwire lists every other
  consumer to update.
- Fork 8's loop-prevention stays structural — strengthened from
  double-omission-from-two-lists to single-absence-of-an-adapter.

Bad / costs:
- One derived-registry indirection in the satellite (an arm list + a mapping)
  in place of two literal lists.
- Axis A's guard is CI-time, not compile-time (C# cannot give the latter for a
  closed sum). The Playwright execution-coverage test costs one test-only
  dependency (NSubstitute, isolated to `WebReaper.Playwright.Tests`), accepted
  because `IPage` is too large to hand-stub.

## Alternatives considered

- **A shared `IBrowserPrimitives` seam in core, one dispatch parameterized over
  it, each transport implementing the primitives.** Rejected: it forces
  Playwright to drop its native methods (`page.FillAsync`,
  `ScrollIntoViewIfNeededAsync`, `Keyboard.PressAsync`) for generic
  evaluate-JS compositions — a capability regression — and it introduces a
  core↔satellite seam that re-litigates ADR-0009. The twin switches are
  correct-by-design (transport-specific bodies); only their *exhaustiveness
  enforcement* was weak, which Axis A's CI coverage test addresses without a new
  seam.
- **Discard-less switch expressions on the transports (the design pass's
  original Axis A).** Rejected: infeasible in C#. A switch expression over a
  class hierarchy is never provably exhaustive to Roslyn, so CS8509 fires even
  when every arm is handled (the `PageAction` private constructor does not help)
  and it cannot be both clean today and a compile error when an arm is added.
  Verified on the .NET 10 SDK. The CI coverage test is the achievable
  substitute. Details in the Decision.
- **An exhaustive `Match`/visitor on `PageAction` in core** (an abstract
  `Match<T>(Func<Click,T>, …)` each arm overrides; both transports dispatch
  through it with native-body lambdas). This *would* give a true compile-time
  guarantee, symmetric across both transports, without generic primitives.
  Rejected for this ADR: it adds a generic visitor to core public API and grows
  per-arm boilerplate (a new arm means a new override plus a signature change
  plus every call site), which cuts against the design pass's "no new core
  machinery, transports keep native bodies" intent. The CI test gives most of
  the protection at a fraction of the surface. Revisit if a third transport
  lands or C# ships discriminated unions.
- **Leave Axis B as-is** (the amendment already co-located per-arm concerns).
  Rejected: the residual four-list sync still allows the silent-drop failure,
  which is invisible (offered to the model, unparseable, no error).
- **Move each arm's full definition into one file across packages.** Impossible
  under ADR-0009 (core cannot reference the satellites); the per-axis approach
  is the most concentration reachable without breaking the quarantine.
