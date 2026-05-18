# The embedded-store local-durable candidate is adopted as the WebReaper.Sqlite satellite, not a core replacement

> Decides the candidate ADR-0011 named ("Named future candidate") and
> issue #58 tracked. The candidate is **adopted**, but its shape is
> **constrained by ADR-0009**: it is a new satellite, not a core
> replacement. The gap between issue #58's prose ("the poll loop and
> position file disappear" — from core) and this outcome is corrected
> below, recorded so it is not silently re-interpreted.

`FileScheduler` hand-rolls a durable queue / write-ahead log: an
append-only job file, an `O(skip N lines)` `StreamReader` resume, a
sidecar position file rewritten after every yield, and a
`Task.Delay(300)` poll. The cursor and the job file are two files that can
desync on a crash between the position write and the next read.
`FileVisitedLinkedTracker` is the same class of local-durable hand-rolling
(an append-only set file plus an in-memory mirror). ADR-0011 named the
idiomatic .NET answer — an embedded store, SQLite via
`Microsoft.Data.Sqlite`, where "resume" is a `SELECT` and concurrency is
the store's — and deliberately did **not** action it, deferring the scope
decision to its own design pass. This is that pass.

## The constraint that decides the shape

`Microsoft.Data.Sqlite` is a managed wrapper over a **native** `e_sqlite3`
shipped through `SQLitePCLRaw.bundle_e_sqlite3` (a per-RID native asset).
It is AOT-*capable* since .NET 8, but it is precisely the **native-interop
dependency class ADR-0009 quarantines out of core** — the same category as
the Cosmos `ServiceInterop` / `vcruntime` graph ADR-0009 names and sheds.
ADR-0009's load-bearing results are: core is dependency-light, the *whole*
core publishes Native-AOT **zero-warning** (proven by
`WebReaper.AotSmokeTest`), and satellites are *deliberately not*
`IsAotCompatible` because keeping native / heavy SDK graphs off core is the
entire point.

Issue #58's literal premise — *replace* the core local-durable roles,
"the poll loop and the position file disappear" — therefore **contradicts
the most recent load-bearing structural ADR**. Putting `Microsoft.Data.Sqlite`
in core re-introduces a native-interop dependency into the default consumer
graph and regresses the ADR-0009 dependency-light + Native-AOT-zero-warning
invariant. The only ADR-0009-consistent shape is a **satellite**. The
honest correction of the issue premise: the core `FileScheduler` poll loop
and position file **do not disappear** — they remain core's zero-dependency
local default; SQLite *supersedes them only for the consumer who opts into
the satellite*.

## Decision

- **Adopt SQLite for the local durable scheduler and visited-link
  tracker, packaged as a new satellite `WebReaper.Sqlite` (the ADR-0009
  mechanism), not a core replacement.** Scope is exactly those two roles
  (the user-confirmed scope); the config blob is excluded — see Bounded
  scope.
- **A third durability tier, opt-in.** File (core, zero-dep, local) stays
  the default; **SQLite (satellite, local, robust — "resume" is a query)**
  is new and opt-in; Redis / Azure Service Bus (satellites, distributed)
  already exist (ADR-0009). SQLite fills the gap ADR-0011 identified: a
  single-machine long crawl that must survive `kill -9` and resume by
  query *without standing up a server* — intrinsically a local concern, so
  forcing Redis/ASB on it is the wrong tier.
- **New types behind the unchanged role interfaces.**
  `SqliteScheduler : IScheduler` and
  `SqliteVisitedLinkTracker : IVisitedLinkTracker`. `IScheduler`,
  `IVisitedLinkTracker`, every core adapter and the builder seam are
  byte-unchanged — the ADR-0009 seam invariant and #58's acceptance
  criterion.
- **Builder sugar as extension methods over the public Registration seam**
  (ADR-0009): `SqliteBuilderExtensions` in namespace `WebReaper.Sqlite`,
  mirroring `RedisBuilderExtensions` exactly — including the optional
  `ILogger` defaulting to `NullLogger` (a satellite extension cannot reach
  the builder's private logger). See "Chosen interface".
- **`Microsoft.Data.Sqlite` is referenced only by `WebReaper.Sqlite`; the
  satellite is *not* `IsAotCompatible`** (ADR-0009 convention) — the
  native `e_sqlite3` / SQLitePCLRaw graph is quarantined there. Core is
  unchanged, so `WebReaper.AotSmokeTest` and the dependency-light core are
  unaffected by construction.

## Mechanism — what actually changes for the opt-in consumer

- **`SqliteScheduler`.** A `jobs(id INTEGER PRIMARY KEY AUTOINCREMENT,
  payload TEXT NOT NULL, consumed INTEGER NOT NULL DEFAULT 0)` table.
  `AddAsync` is an `INSERT`; `GetAllAsync` reads `WHERE consumed = 0 ORDER
  BY id` and marks a row `consumed = 1` **before it yields it** — the
  claim is committed first.
  **The position file and the `O(skip N lines)` `StreamReader` cursor are
  gone — resume is `SELECT … WHERE consumed = 0`.** The cursor↔job-file
  desync failure mode is *unrepresentable*: one store, one transaction.
  This is claim-before-yield, the existing `IScheduler` family contract:
  `FileScheduler` advances its position cursor *before* the `yield`
  (`FileScheduler.cs`) and `RedisScheduler` pops destructively
  (`ListLeftPop`) before its `yield`; the role interface has no ack, so
  "consume = claim" *is* the contract, not a choice. On `kill -9` every
  not-yet-claimed row is intact and re-yielded by the same `WHERE consumed
  = 0` query; the single in-flight job is not re-yielded — the *same*
  at-most-once-for-the-in-flight-job guarantee the whole family already
  has, and no weaker. (Correction: an earlier draft of this clause said
  `FileScheduler`'s position file is written *after* the yield — it is
  not; the family is claim-before-yield. The decision and shape are
  unaffected; only this rationale aside is fixed — recorded, per the
  repo's "called out loud, never silent" rule, in PR #66.)
- **The Job grammar is unchanged.** The payload is
  `WebReaperJson.SerializeJob` / `DeserializeJob` — the *same ADR-0008
  grammar* as `FileScheduler` and `RedisScheduler`. ADR-0008's uniform
  `Job` round-trip across every `IScheduler` is preserved, not
  re-litigated.
- **Honest scope — the empty-wait does *not* disappear.** `GetAllAsync`
  is still an infinite stream that must wait when the queue is momentarily
  drained, because jobs are produced concurrently by in-flight crawl tasks
  (`RedisScheduler` also still `Task.Delay(300)` polls `ListLeftPop`).
  What disappears is the **position file**, the **line-skip resume**, and
  the **cursor desync risk** — not the producer/consumer wait. Whether the
  wait is a short indexed `WHERE consumed = 0` poll or an in-process signal
  is an implementation choice for the derived issue, *not* an ADR
  decision; the ADR claims only what is true. `Complete()` keeps the
  `IScheduler` default no-op (matches `FileScheduler`; termination stays
  crawl-limit / cancellation, ADR-unrelated).
- **`SqliteVisitedLinkTracker`.** A `visited(url TEXT PRIMARY KEY)` table.
  `AddVisitedLinkAsync` = `INSERT OR IGNORE`; `GetVisitedLinksCount` =
  `SELECT COUNT(*)`; `GetNotVisitedLinks` = an anti-join; `GetVisitedLinksAsync`
  = `SELECT url`. **Deliberate deviation from `FileVisitedLinkedTracker`,
  recorded: no full in-memory `ConcurrentBag` mirror — the table *is* the
  set, queried directly, mirroring the satellite sibling
  `RedisVisitedLinkTracker`.** Load-bearing reason: the mirror *is* the
  "load the entire visited set into process memory at start" that an
  embedded durable store exists to remove; it does not survive the
  very-large-crawl scale at which a durable store is chosen; and shape
  consistency across the *durable tier* (Redis and SQLite both "the store
  is the source of truth") is the right consistency, not consistency with
  the zero-dep file adapter whose mirror is *its* essence. The `PRIMARY
  KEY` index keeps the per-page `GetNotVisitedLinks` fast and the crawl is
  page-fetch-I/O-bound regardless.
- **Concurrency is the store's.** SQLite WAL mode + write serialization
  absorbs the engine's `Parallel.ForEachAsync` fan-out (`AddAsync` from
  many crawl tasks while `GetAllAsync` consumes). This is the ADR-0011
  thesis realized — *not* `FileScheduler`'s hand-rolled `SemaphoreSlim(1,1)`,
  and *not* the rejected ADR-0011 held-handle / shared-lock substrate.

## Bounded scope — what this does NOT change

- **Core is byte-unchanged.** `FileScheduler`, `FileVisitedLinkedTracker`,
  `FileBlobStore`, `FilePersistencePrep`, the role interfaces,
  `ScraperEngine`, the builder seam — no edits, no added
  `PackageReference`. ADR-0011's prep helper and its preserved ADR-0006
  fence stand untouched. The core file-as-queue poll loop **remains** as
  the zero-dep default — it is *superseded only for the opt-in consumer*,
  the explicit correction of issue #58's "disappears from core" prose.
- **The config blob stays on `FileBlobStore`** (user-confirmed scope).
  Whole-file, write-once at build / read-once at run start, no poll loop,
  ADR-0003-governed — an embedded store buys it nothing. A
  `SqliteBlobStore` / Sqlite config storage, and a Sqlite *sink*, are
  **named, explicitly-not-actioned future candidates** (the
  ADR-0004/0005/0006/0009 "named, distinct future candidate" posture) so
  they are neither silently in scope nor re-proposed.
- **ADR-0009.** Its satellite invariant is preserved and *extended* — one
  more satellite, same mechanism, same `IsAotCompatible`-off convention,
  same extension-over-public-seam wiring. Not amended.
- **ADR-0008** (uniform `Job` round-trip — same `WebReaperJson`),
  **ADR-0011** (prep helper; rejected substrate; preserved ADR-0006
  fence), and **ADR-0001–0007/0010** mechanisms are untouched.

## SemVer

**Purely additive.** A new satellite package `WebReaper.Sqlite` (its own
version, aligned with the other satellites' line) and extension methods
over the *existing* public Registration seam. **Zero** change to core's
public surface or to the core package's dependency graph. Nothing is
removed, so — unlike ADR-0009's deliberate clean-cut — there is no
compat-shell question at all. The core package version is unaffected.

## Considered options

- **SQLite as the `WebReaper.Sqlite` satellite, scheduler + tracker, file
  stays the core default (chosen).** Delivers ADR-0011's idiomatic
  embedded store for the real local-durable niche, preserves every prior
  ADR (especially the ADR-0009 dependency-light / AOT-zero-warning core),
  purely additive. The honest reading of "adopt the candidate" once
  ADR-0009 constrains its shape.
- **SQLite replaces the file adapters in core (rejected).** Issue #58's
  literal prose. Drags native `e_sqlite3` / SQLitePCLRaw into the default
  consumer graph and regresses the ADR-0009 dependency-light +
  Native-AOT-zero-warning core invariant — refuted by the most recent
  load-bearing ADR. Recorded so the core-replacement shape is not
  re-proposed.
- **Decline entirely (rejected — its strongest form recorded).**
  `IScheduler` already has four adapters; post-#56/#57 `FileScheduler` is
  *correct* and the poll loop is a cosmetic, not a correctness, smell, so
  a fifth adapter must clear the LANGUAGE.md "earn its keep" / deletion-test
  bar, and Redis/ASB already serve durable resume. Rejected because that
  bar *is* cleared: the narrow but real niche — a single-machine long
  crawl surviving `kill -9` and resuming by query without a server — is
  exactly where the line-skip resume and cursor↔job-file desync bite, and
  Redis/ASB force a server onto an intrinsically local concern (the wrong
  tier, not a substitute). The decline reasoning is recorded so neither
  the core-replacement nor the do-nothing shape is re-suggested.
- **An in-memory mirror in `SqliteVisitedLinkTracker` (rejected).**
  Re-introduces the load-everything-into-process-memory an embedded
  durable store exists to remove and breaks durable-tier shape
  consistency with `RedisVisitedLinkTracker`. The mirror is the *file*
  adapter's essence and stays there.
- **Scope including the config blob and/or a Sqlite sink (deferred,
  named).** No poll loop, ADR-0003-governed write-once/read-once payload;
  recorded above as a named future candidate, not actioned.

## Derived implementation issues (AFK-able, ADR-0009 pattern, guardrail green at each)

1. **`WebReaper.Sqlite` satellite + `SqliteScheduler`.** New project
   (csproj mirroring `WebReaper.Redis`: `net10.0`, *not* `IsAotCompatible`,
   `CS1591` `NoWarn`, `ProjectReference` core, `Microsoft.Data.Sqlite`
   `PackageReference`, package metadata + README), added to
   `WebReaper.sln`. `SqliteScheduler : IScheduler` (schema +
   `Initialization`; `DataCleanupOnStart`; `AddAsync` ×2; `GetAllAsync`
   consume-by-query; `WebReaperJson`). `SqliteBuilderExtensions.WithSqliteScheduler`.
   `WebReaper.Sqlite.Tests` (mirroring `WebReaper.Redis.Tests`) pinning
   the `IScheduler` contract: `Job` round-trip, **resume-after-reopen**
   (the position-file-killer test), `DataCleanupOnStart`.
2. **`SqliteVisitedLinkTracker` + `TrackVisitedLinksInSqlite`** in the
   satellite. Tests pinning the `IVisitedLinkTracker` contract including
   the deliberate no-mirror behaviour (count / not-visited served from the
   table; survives reopen).
3. **Consumer-facing docs (land with the implementation).** Additive
   CHANGELOG section (mirroring the 7.0.0 satellite entry shape), README
   satellite mention, optional `WithSqliteScheduler` example. *Not* the
   CONTEXT.md / ADR-0011 flip: the "named future candidate → decided"
   cross-reference (CONTEXT.md "File persistence prep" term + the
   Flagged-ambiguities entry + the ADR-0011 "Named future candidate"
   note) lands **with this ADR-0012 PR**, not here — the ADR-0008/0009/0011
   precedent that the deciding doc and CONTEXT move together. A CHANGELOG
   entry for unbuilt code would be wrong, so it waits for slice 1/2.
