# RedisBase is retired: one Redis connection pool, one multiplexer per connection string

ADR 0003 bounded a finding it could not fully close: `RedisBase`'s
process-`static`, first-connection-wins `ConnectionMultiplexer`. The first
`RedisBase` subclass constructed anywhere in the process connected; every later
connection string was silently ignored (`if (isInitialized) return;`) and every
subclass shared that one multiplexer — a latent bug for the distributed mode
the README sells. ADR 0003 fixed it *on the config/cookie path only*, by
proving the per-connection-string pattern in `RedisBlobStore`, and named full
retirement an explicit follow-up. This is that follow-up.

The per-connection-string multiplexer mechanism now has exactly one home:
`RedisConnectionPool` — a concrete module owning a
`ConcurrentDictionary<string, Lazy<ConnectionMultiplexer>>` and the connect
tuning (`AbortOnConnectFail`/`AllowAdmin`/timeouts/`ExponentialRetry`). All
four Redis adapters resolve through it: `RedisBlobStore`, `RedisScheduler`,
`RedisSink`, `RedisVisitedLinkTracker`. `RedisBase` is deleted. Its
`SerializeToJson` moves into `RedisScheduler` (its only caller), verbatim.

`RedisBlobStore` is migrated too, deliberately. Leaving its inline copy of the
dictionary + connect config would mean the mechanism still had two homes — the
deletion test would still fail (delete the pool and the config reappears
duplicated across four classes; before this change `RedisBlobStore` *was* that
inline copy — the proof). Adopting the pattern means *sharing* it, not
re-copying it. With four consumers behind one tiny interface
(connection string ⇒ multiplexer), this is a deep module / real seam by the
skill's test — not indirection without variation.

Concrete, not an `IRedisConnectionPool`. There is exactly one way to pool
StackExchange.Redis multiplexers; the variation is in the *consumers*, not in
implementations. A one-implementation interface here would be the
single-adapter indirection ADR 0001 and ADR 0004 reject (the same reasoning
that deleted `IStaticPageLoader`). Composition, not a fixed base class: the
three retired classes are unrelated kinds — a set store, a queue, an append
sink — sharing only "needs a Redis connection." Inheritance modelled that as an
*is-a*; ADR 0003 already rejected "shared base/helper only" for the analogous
persistence cluster. They now *hold* a database resolved from the pool.

Verified, not inferred: the official StackExchange.Redis docs state
"**AbortOnConnectFail** (bool) — If true, Connect will not create a connection
while no servers are available. Default: true." The pool sets it `false`, so
`ConnectionMultiplexer.Connect` returns a background-reconnecting multiplexer
instead of throwing when no server is reachable. That contract is what makes
the offline identity test safe — same connection string ⇒ same multiplexer,
different string ⇒ different multiplexer, the precise behaviour `RedisBase`'s
static first-wins got wrong and was *untestable* (process-global, no reset
between tests). It is the project's first Redis-touching unit test, justified
because it pins exactly the bug this work fixes; it uses dead loopback
endpoints and never needs a server.

## Bounded scope — what this does NOT fix

`RedisScheduler` serialises a `Job` with `TypeNameHandling.None`
(`SerializeToJson`) and deserialises it with default settings — the same
serialize/deserialise asymmetry ADR 0003 fixed for the config payload, where a
`Job`'s `ImmutableQueue<LinkPathSelector>` and `PageAction.Parameters`
(`object[]`) need type metadata to rematerialise. It is **preserved verbatim**
here: this candidate is the *multiplexer* retirement, not a scheduler
serialization fix. Job round-trip fidelity in the distributed scheduler is a
distinct finding, surfaced (the comment in `RedisScheduler.SerializeToJson`
says so) and left for a future candidate. Conflating the two would repeat the
mistake of un-bounding a deepening.

## SemVer

`RedisBase` was `public`; removing a public type is breaking on paper. In
practice it exposed only a `protected` constructor and `protected static`
fields — no external subclass can use it meaningfully. It rides this branch's
already-major release (Candidate 5 / ADR 0004). Net public surface added:
`RedisConnectionPool`.

## Considered options

- **One `RedisConnectionPool`, all four adapters through it (chosen).**
  Mechanism one home; the first-wins bug becomes unrepresentable for every
  Redis adapter at once; the fix is unit-pinned for the first time.
- **Inline the per-connection-string dictionary into each class (rejected).**
  Duplicates the mechanism across four classes — the exact anti-pattern ADR
  0003 fixed for persistence, re-introduced. Fails the deletion test by
  construction.
- **Keep a base class, fix it to be per-instance (rejected).** Still
  inheritance for "needs a connection" across three unrelated kinds; ADR 0003's
  already-rejected "shared base/helper only, keep N classes" shape. A
  connection is a collaborator to hold, not a superclass to be.
