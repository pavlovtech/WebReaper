# File persistence prep is one small stateless helper; the held-handle substrate is rejected as un-idiomatic

> Supersedes an earlier working draft of this ADR
> (`0011-durable-file-substrate.md`, never committed) that proposed a
> stateful *Durable file substrate*. That draft is rejected; the reasoning
> is recorded below ("The rejected substrate") so it is not re-proposed.

Four file-backed adapters re-derived the same *pre-write* file handling.
`FileBlobStore`, `FileScheduler`, `FileVisitedLinkedTracker`, and
`BufferedFileSink` each, independently, answer: *does the directory exist
before I write / does `DataCleanupOnStart` happen and when / is a missing
file an error on read*. As ADR 0002/0003/0006 found three times, the copies
drifted into bugs that exist in only one copy:

- `FileBlobStore.PutAsync` is `File.WriteAllTextAsync(key, value)` — no
  directory creation; throws on any nested path, contradicting its own
  "the key *is* the file path" doc.
- `FileVisitedLinkedTracker` creates the directory **only inside**
  `if (!File.Exists(_fileName))` — throws if the file exists but its
  directory was since removed.
- `FileScheduler`/`BufferedFileSink` create it eagerly and unconditionally
  — the correct behaviour, in only two of four. `DataCleanupOnStart`
  timing (eager vs lazy) diverged the same way.

That shared, bug-prone behaviour is **small and stateless**: ensure the
directory exists (eager, unconditional — `Directory.CreateDirectory` is
idempotent), apply `DataCleanupOnStart` deterministically at start, read a
missing file as empty rather than throwing. It gets one home: a small
stateless **File persistence prep** helper (CONTEXT.md "Crawl-state
persistence"). Each adapter keeps its own write/read essence
(`FileBlobStore` whole-file replace, `FileScheduler` its append + resumable
cursor, the tracker its append + in-memory mirror, the sink its ADR-0006
drain) and only delegates the prep.

Deletion test: delete the helper and directory-creation + cleanup-timing +
missing-file policy reappear duplicated across four adapters with the three
single-copy bugs. Complexity (and the bugs) concentrate in one place — it
earns its keep. It is deliberately *thin* — a stateless helper, not a deep
module; the leverage is locality of a bug-prone cross-cutting policy, the
honest and proportionate shape for what is genuinely shared.

## The rejected substrate (recorded so it is not re-proposed)

An earlier draft made this a stateful **Durable file substrate**: one
long-lived write handle per path, write-through flush, a shared
single-writer `SemaphoreSlim`, and it **superseded ADR 0006's deferred
open/close-churn fence**. Rejected, two load-bearing reasons:

1. **It standardizes the *less* idiomatic .NET concurrency pattern.** The
   idiomatic answer to "many producers, one file" in .NET is a single
   writer fed by a queue (`System.Threading.Channels.Channel<T>` or
   `BlockingCollection<T>` + one consumer) — exactly what
   `BufferedFileSink`'s drain already is, and how mainstream file logging
   sinks are built. A shared `SemaphoreSlim`-per-write substrate
   standardizes on the weaker pattern and forces the one already-idiomatic
   adapter (`BufferedFileSink`) to *opt out* of the shared mechanism — the
   opt-out we had treated as a minor wart was the abstraction pointing the
   wrong way.
2. **The held handle / write-through re-opened bounded scope.** It pulled
   ADR 0006's deferred per-row open/close churn back in, plus a
   process-lifetime handle and dispose contract — exactly the un-bounding
   ADR 0005/0006 warned against. **ADR 0006's fence therefore stands**:
   per-row open/close is still its own deferred candidate, untouched here,
   not closed or superseded. The earlier draft's declared "`FileBlobStore`
   replace becomes serialised" consequence is **withdrawn** — no shared
   lock is introduced, so there is no behaviour change.

Concurrent durable append remains a single-writer concern. This ADR does
**not** build a shared single-writer component (that would re-introduce the
same lifetime scope for the scheduler/tracker that ADR 0006 deferred for
the sink); standardizing the hand-rolled `SemaphoreSlim` adapters onto the
established single-writer pattern is a direction, pursued — if at all —
through the embedded-store candidate below, not by a new core mechanism.

## Named future candidate (not actioned here)

> **Update — decided (ADR-0012).** This candidate is no longer open:
> adopted as the new **`WebReaper.Sqlite` satellite**, *not* a core
> replacement. `Microsoft.Data.Sqlite` is a native-interop dependency
> (native `e_sqlite3` via SQLitePCLRaw) — the exact class ADR-0009
> quarantines off core — so the core `FileScheduler` poll loop + position
> file *stay* as the zero-dependency default; SQLite is an opt-in
> robust-local tier (scheduler + visited-link tracker). The "the poll loop
> and position file disappear" framing below is the candidate's *original*
> prose; ADR-0012 corrects it (they disappear only for the opt-in
> consumer). See `docs/adr/0012-sqlite-embedded-store-satellite.md`.

`FileScheduler`'s **file-as-queue** is the deeper smell, and the prep helper
deliberately does not touch it: an append-only job file polled with a
`Task.Delay(300)` loop, with a sidecar position file tracking the read
cursor, reimplements a durable queue / write-ahead log by hand. The
idiomatic .NET answer for resumable local durable state is an embedded
store — SQLite via `Microsoft.Data.Sqlite` — where "resume" is a query,
concurrency is the engine's, and the poll loop and position file disappear;
the distributed case already has the satellited Redis / Azure Service Bus
schedulers (ADR 0009). Moving the local durable roles (scheduler resume,
visited-link set, possibly the config blob) onto an embedded store is a
distinct, larger candidate that contradicts none of ADR 0001–0010 (those
govern seams and serialization, not the local-durability mechanism). It is
recorded here and in CONTEXT.md as a named future candidate — the ADR
0004/0005/0006/0008 "named, distinct future candidate" posture — and is the
genuinely strategic improvement the rejected substrate was a roundabout way
of avoiding.

## Bounded scope — what this does NOT change

- **Role interfaces and public constructors.** `IScheduler`,
  `IVisitedLinkTracker`, `IKeyedBlobStore`, `IScraperSink` and the four
  adapters' public constructors are unchanged — source-compatible.
- **Each adapter's write/read essence.** Whole-file replace, the resumable
  cursor + position file + poll loop, the in-memory mirror, the ADR-0006
  `BlockingCollection` drain + `IFileSinkFormat` are all untouched. Only
  directory/cleanup/missing-file prep is centralized.
- **ADR 0006.** Its fence stands; per-row open/close is not closed here.
- **ADR 0001/0002/0003/0004/0005/0008/0009/0010 mechanisms.** Untouched.

## SemVer

**Patch / minor.** A new internal stateless helper plus three bug fixes
(`FileBlobStore` and `FileVisitedLinkedTracker` directory creation,
`DataCleanupOnStart` timing). No public surface change, no lock introduced,
no declared behaviour regression — the previously-throwing nested-path case
now succeeds, which is strictly a fix.

## Considered options

- **One small stateless File persistence prep helper (chosen).**
  Proportionate to what is genuinely shared and bug-prone; three single-copy
  bugs become unrepresentable; no lifetime, no lock, no public surface
  change; ADR 0006's fence preserved.
- **Stateful Durable file substrate, held handle + write-through + shared
  lock, supersedes ADR 0006 (rejected).** Standardizes the un-idiomatic
  concurrency pattern, forces the idiomatic adapter to opt out, and
  re-opens the ADR 0005/0006 lifetime scope. The .NET-idiom rejection
  recorded above so it is not re-suggested.
- **Move local durable roles to an embedded store / SQLite (deferred,
  named).** The idiomatic answer for resumable local durable state and the
  real fix for the file-as-queue smell; a distinct, larger candidate,
  recorded above, not actioned here.
- **Fix the call sites in place, no shared helper (rejected).** Smallest
  diff, but leaves the policy decided per-adapter — the ADR 0002/0003
  already-rejected "no shared home" shape; the deletion test shows the
  policy concentrates, so it deserves one home (a stateless one).
