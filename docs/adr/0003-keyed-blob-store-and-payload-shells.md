# Persistence is one keyed blob store; payloads quarantine their own quirks in shells

The config and cookie persistence stores were **eight** near-duplicate classes:
`IScraperConfigStorage` and `ICookiesStorage`, each realized over the same four
backends (in-memory, file, Redis, Mongo). The pairs were character-identical
modulo the payload type, and — as ADR 0002 found for the Schema walk — the
duplication had already drifted into real bugs that existed in only one copy.

The persistence mechanism now has one home per backend: a **keyed blob store**
seam (`Put`/`Get` of one opaque UTF-8 string under a key, last-write-wins,
`null` ⇔ absent) with exactly four adapters. Serialization and every
payload-specific quirk live *above* the seam in a thin **payload shell** — the
config shell owns `TypeNameHandling.Auto`, the cookie shell owns the
`CookieContainer ↔ CookieCollection` mapping. The four adapters never know which
payload they hold. `IScraperConfigStorage` / `ICookiesStorage` are kept as the
shells' interfaces, so the fluent builder and all consumer code are unchanged:
additive public surface, source-compatible, **minor SemVer** — exactly ADR
0002's posture.

This is ADR 0002 re-applied to a second cluster. The shared grammar there was
the Schema fold and quirks were quarantined at `ExtractRaw`; here the shared
grammar is *upsert a value under a key / fetch it / one missing policy* and the
quirks are quarantined in the payload shell. The deletion test passes the same
way: delete the seam and four genuinely different persistence mechanisms reappear
duplicated across two payloads — a real seam (four adapters, two payloads), not
the indirection-without-variation ADR 0001/0002 reject.

Why a `string` blob and not a typed `IDocumentStore<T>`: a typed seam promises
"any `T` round-trips," and `System.Net.CookieContainer` breaks that promise —
that is *why* the file cookie adapter detours through `GetAllCookies()` today. A
typed seam would still need a cookie DTO and a mapping; put that in a shell and
the typed seam is just this design with a redundant wrapper, put it in the seam
and the seam knows about cookies — the quirk is no longer quarantined, defeating
the property the design exists to protect. There is no binary payload and none
foreseen; `byte[]` would be generality without variation.

Partially addresses, and bounds, the separately-surfaced `RedisBase` finding.
`RedisBase`'s process-`static` single `ConnectionMultiplexer` (bound to whichever
connection string connected first, silently ignoring the rest — a latent bug for
the distributed mode the README sells) is **bypassed on the config/cookie path**:
the Redis blob-store adapter owns its multiplexer per connection string, no
statics. `RedisBase` is **not** retired — `RedisVisitedLinkTracker`,
`RedisScheduler`, and `RedisSink` still extend it and still carry the bug; those
are out of this candidate's scope (a set store, a queue, an append sink — not
keyed blob stores). The net effect is to shrink that finding from five classes
to exactly three and to prove the per-connection-string fix pattern they would
later adopt. Full `RedisBase` retirement is an explicit follow-up, not claimed
here.

## Deliberate behaviour unifications (not regressions — see CONTEXT.md "Flagged ambiguities")

- **Mongo stores an opaque `{ id, blob }`, not a queryable BSON projection.**
  WebReaper only ever fetches a whole config/cookie set by key and never queries
  inside it; the BSON shape was never load-bearing. Do not "restore" it.
- **Uniform missing-value policy.** `null` ⇔ absent at the store. The config
  shell throws a typed not-found (preserving the file adapter's fail-fast
  intent, now uniform and meaningful); the cookie shell returns an empty
  `CookieContainer` (what a fresh crawl wants). Replaces the file adapter's
  `NullReferenceException` and the silent-`null` divergence in the others.
- **`Put` is upsert-by-key.** Fixes the Mongo adapters' `InsertOneAsync` +
  `FirstOrDefault` append-and-read-oldest bug.
- **`ScraperConfig` round-trips with `TypeNameHandling.Auto` through every
  backend.** `ScraperConfig` carries `PageAction.Parameters` (typed `object[]`,
  genuinely polymorphic) and an `ImmutableQueue<LinkPathSelector>` (needs type
  metadata to rematerialize). `RedisBase.SerializeToJson` used
  `TypeNameHandling.None`, so Redis was silently lossy for both; the file
  adapter additionally serialized with `Auto` but *deserialized with defaults* —
  an asymmetry even within one backend. The setting now has one home, applied
  symmetrically (the config shell).
- **The broken Mongo cookie read** (`.ToJson()` on the `FindAsync` cursor
  instead of the document) cannot recur — the adapter no longer touches BSON.
- **In-memory storage now round-trips through the payload shell's serializer**
  like every other backend, instead of holding the live object. Intentional: it
  makes the fast in-memory path (used by most tests and the default builder)
  exercise the *same* serialization the persistent backends do, so a
  serialization regression fails a cheap unit test instead of only a flaky
  integration test. `Get` now returns a copy, not the stored reference; no
  caller relied on reference identity.

## Considered options

- **One keyed blob store + per-payload shells (chosen).** Persistence
  mechanism one home per backend; serialization and quirks quarantined in the
  shell; the four latent bugs become unrepresentable; one payload-free contract
  test matrix over four adapters plus one focused quirk test per shell.
- **Typed `IDocumentStore<T>` owning serialization (rejected).** Cleaner call
  sites, but `CookieContainer` breaks the round-trip promise, so a DTO + mapping
  is unavoidable; placing it in a shell collapses to the chosen design with a
  redundant generic wrapper, placing it in the seam un-quarantines the quirk.
  The call sites are ~1–2 per payload behind the builder — generic ergonomics
  buy almost nothing here.
- **Shared base/helper only, keep eight classes (rejected).** Smallest change,
  but leaves the real duplication (missing policy, upsert, serializer settings)
  decided per-class — ADR 0002's already-rejected "node-navigator port,
  everything internal" shape.
