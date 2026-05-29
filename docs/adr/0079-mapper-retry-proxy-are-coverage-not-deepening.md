# 0079. Site Mapper, Retry Policy, and Proxy Provider Are Coverage Gaps, Not Deepening Targets

- **Status:** Accepted
- **Date:** 2026-05-29
- **Deciders:** Alex (HITL), Claude (architecture pass)

## Context

An architecture-deepening review applied the "the interface is the test surface"
lens and flagged three modules as candidates: the **Site mapper** (`SiteMapper`,
292 loc, no tests), the **Retry policy** (`FixedAttemptsRetryPolicy`, untested),
and the validated proxy provider (`ValidatedProxyProvider`, reported untested
and reported to sit behind a single-adapter `IProxyProvider` seam). The implied
deepening was "put the I/O behind a seam so the logic is testable offline."

Verifying each against source changed the picture.

## Decision

We will treat these as **test-coverage gaps, not architectural deepening
targets**, and record why so future reviews do not re-propose restructuring
them.

- **Site mapper.** The seam already exists: `SiteMapper`'s constructor takes a
  `Func<HttpMessageHandler> handlerFactory` (invoked once per `MapAsync`),
  explicitly "for proxy support / test substitution." The
  robots.txt → sitemap → index-recursion → root-link → union → host/substring
  filter → cap logic is a `local-substitutable` dependency: a stub handler
  drops in and the whole best-effort decision table is testable offline through
  `MapAsync` today. It simply has no tests.
- **Retry policy.** A small module behind `IRetryPolicy` (ADR-0026), testable
  through `ExecuteAsync` with a counting delegate — the four-attempts /
  never-retry-`OperationCanceledException` contract is load-bearing and
  unpinned, but the seam is already the test surface.
- **Validated proxy provider.** Already tested — `ProxyValidationTests` exercises
  validation, refresh, timeout, and reuse-on-empty through `IProxySource` /
  `IProxyValidator` fakes. And `IProxyProvider` already has **two** adapters:
  `ValidatedProxyProvider` (core) and `WebShareProxyProvider`
  (`Misc/WebReaper.ProxyProviders`). It is a real seam, not single-adapter; the
  original "untested / single-adapter" finding was wrong.

The action item is to add offline tests for the **Site mapper** and the **Retry
policy** (tracked as separate tasks), not to restructure any of the three.

## Consequences

Good:
- Future architecture reviews will not re-suggest deepening these — the record
  states the seams already exist and the proxy provider is already a tested
  two-adapter seam.
- The remaining work is correctly scoped as fast offline coverage, landing in
  the unit-test suite rather than relying on the live-site integration tests.

Bad / costs:
- None architectural. The coverage gap remains until the spawned test tasks
  land.

## Alternatives considered

- **Introduce a port for the Site mapper's HTTP fetch.** Rejected: the seam
  (`handlerFactory`) already exists; adding another would be indirection without
  a second adapter.
- **Inline `ValidatedProxyProvider` into the transport** (the "shallow
  single-adapter seam" suggestion). Rejected: it is a genuine two-adapter seam
  (`ValidatedProxyProvider` + `WebShareProxyProvider`) and is already tested;
  inlining would delete a real extension point.
- **A shared stub-`HttpMessageHandler` test fixture** across the Site mapper and
  any other live-HTTP modules. Noted as a reasonable convenience for the
  coverage tasks, but deferred — it is a test-project helper, not an
  architectural change to the library.
