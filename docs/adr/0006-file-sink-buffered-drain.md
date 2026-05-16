# File sinks share one buffered drain; the format is the only quarantined quirk

`JsonLinesFileSink` and `CsvFileSink` were two `IScraperSink`s that copied the
same buffered-drain mechanism: a `BlockingCollection<JObject>` producer
(`EmitAsync`), a single background consumer that appends each entry to a file,
a one-time init that optionally honours `DataCleanupOnStart`, and the
`url`-injection. As ADR 0002/0003 found for the Schema walk and the persistence
stores, the copies had **drifted into divergent bugs that existed in only one
side**:

- **Init token / lifecycle.** JSON-lines called `Init()` from its constructor
  bound to `CancellationToken.None`; its `EmitAsync` re-init was dead code
  (`IsInitialized` was already set). CSV inited lazily on first `EmitAsync`
  with the caller's token. Different lifecycles by drift.
- **Directory creation.** JSON-lines created the directory — but only inside
  the `DataCleanupOnStart` branch. CSV never created it. A sink to a
  not-yet-existing directory threw on the first write (always for CSV; for
  JSON-lines whenever not cleaning).
- **`DataCleanupOnStart` timing.** JSON-lines deleted eagerly at construction
  (deterministic, and it happened even for a crawl that produced zero rows).
  CSV deleted lazily inside the first `EmitAsync`, so an empty crawl kept the
  stale file.
- **Concurrency.** CSV's `async` init guard straddled an `await` (the header
  write), so concurrent first-emits — the normal case under the Spider's
  `Parallel.ForEachAsync` sink fan-out — could double-write the header and
  spawn two consumers racing the same file. JSON-lines spawned its consumer in
  the constructor, dodging that race but keeping the wrong token.

The only *legitimate* difference is per-row rendering: JSON-lines emits one
compact object per line; CSV emits a header derived from the first row's
flattened leaf names, then quoted, comma-joined rows. That is exactly the ADR
0002/0003 shape a third time: a shared grammar (the drain) with one
quarantined quirk (the format).

The buffered drain now has one home, `BufferedFileSink`. The quirk lives
behind `IFileSinkFormat` (`string? Header(JObject firstRow)` — `null` ⇒ no
header; `string FormatRow(JObject row)`), with the JSON-lines and CSV
flatten/quote/`Formatting.None` expressions **moved verbatim** into
`JsonLinesFormat` / `CsvFormat`. `JsonLinesFileSink` / `CsvFileSink` remain as
thin `BufferedFileSink` subclasses with their existing public ctors — the ADR
0003 compat-shell move — so `SpiderBuilder` and all consumer code are
unchanged: **source-compatible, minor SemVer**.

Deletion test: delete `BufferedFileSink` and the queue + background consumer +
init/cleanup/spawn reappears duplicated across both file sinks (and any future
one — TSV, NDJSON). Complexity concentrates in the module: it earns its keep.
An interface for the format is justified here — there are **two real adapters**
(unlike ADR 0005's single Redis pooling mechanism, which stayed concrete); the
variation is genuine, not the single-adapter indirection ADR 0001/0004 reject.

## Deliberate behaviour unifications (not regressions — see CONTEXT.md "Flagged ambiguities")

- **Cleanup and directory creation are eager (constructor) and
  unconditional for the directory.** Both formats now clean deterministically
  before any write — correct even for a zero-row crawl — and the directory is
  created whether or not `DataCleanupOnStart` is set. This generalises the
  better of the two old behaviours and removes the CSV empty-crawl stale-file
  bug and both directory bugs.
- **One consumer, started once, bound to the first `EmitAsync`'s token.**
  Thread-safe double-checked init replaces JSON-lines' dead re-init /
  `CancellationToken.None` and CSV's double-spawn race. Observable file
  content is unchanged (header, if any, then rows in arrival order).

## Bounded scope — what this does NOT change

One `File.AppendAllTextAsync` per row opens and closes the file every time,
and the consumer still has no flush/dispose (its lifetime is the process or
the bound token) — exactly as before. These are *shared* properties of the old
code, not duplication this candidate removes; widening scope to a batched
writer or a disposable lifetime would repeat ADR 0005's warning about
un-bounding a deepening. Named here and in CONTEXT.md as a separate future
candidate.

## Considered options

- **One `BufferedFileSink` + `IFileSinkFormat`, two thin compat subclasses
  (chosen).** Mechanism one home; the four divergent bugs become
  unrepresentable; the format is one pure, trivially tested seam; public
  surface and builder unchanged.
- **A shared abstract base the two sinks inherit (rejected).** The drain would
  still be entangled with each format by inheritance and protected state — ADR
  0003's already-rejected "shared base/helper only" shape; the format is a
  collaborator to hold, not a superclass to be (cf. ADR 0005).
- **Delegates instead of an interface (rejected).** Two `Func`s carry no name;
  `IFileSinkFormat` with two real implementations is the readable seam and
  matches the `ISchemaBackend` precedent (ADR 0002).
