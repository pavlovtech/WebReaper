# The serialization grammar is System.Text.Json source-gen; Newtonsoft `JObject` + `TypeNameHandling.Auto` are superseded

The extraction and persistence pipeline ran on `Newtonsoft.Json` across **21
core files**. Two distinct uses, one root cause:

- **The Schema fold's terminal is a `JObject`.** `IContentParser.ParseAsync`
  returns `Newtonsoft.Json.Linq.JObject`; `SchemaContentParser<TNode>` builds
  `JObject`/`JArray`/`JToken` throughout and `Coerce` ends in
  `JToken.FromObject`. That `JObject` is then *public surface* far past the
  fold: `ParsedData(string Url, JObject Data)`, `IFileSinkFormat.Header(JObject)`
  / `FormatRow(JObject)`, and `PostProcess(Func<Metadata, JObject, Task>)` on
  both builders.
- **The config payload shell round-trips with `TypeNameHandling.Auto`.**
  `ScraperConfigStore` and `AzureServiceBusScheduler` embed CLR `$type` strings
  to rematerialise the genuinely-polymorphic members `ScraperConfig` carries —
  `PageAction.Parameters` (`object[]`) and the `ImmutableQueue<LinkPathSelector>`
  selector chain (ADR 0003). `RedisScheduler` serialises a `Job` with
  `TypeNameHandling.None` and reads it back with defaults — the
  serialize/deserialise asymmetry ADR 0003 fixed for the config payload and ADR
  0005 explicitly named, surfaced, and left as a distinct future candidate for
  the scheduler.

`TypeNameHandling` is fundamentally AOT-hostile: it resolves arbitrary types by
name through reflection at deserialise time — exactly the pattern Native AOT
trimming cannot see (`IL2026`/`IL3050`). It is also a proven bug source in this
codebase, not a hypothetical one: ADR 0003 found the file adapter serialised
with `Auto` but *deserialised with defaults*; ADR 0005 found `RedisScheduler`
still does. Verified, not inferred: there is no
`Activator.CreateInstance`, `MakeGenericType`, `Type.GetType`, or `dynamic`
*in `WebReaper`'s own code*, and no `.csproj` sets `PublishAot`, so the
*application-code* AOT problem is bounded to serialization. But Newtonsoft is
AOT-hostile on **two** independent axes, and the Phase-0 spike proved the
second one empirically (see "Discovered constraint" below): (1) the
`TypeNameHandling.Auto` reflection-by-name on the config/scheduler path —
removed here; and (2) the assembly itself — `JToken`/`JObject`/`JValue`
implement `IDynamicMetaObjectProvider` (`GetMetaObject`, DLR) and
`IConvertible.ToType`→`JToken.ToObject(Type)`, all `RequiresUnreferencedCode`/
`RequiresDynamicCode`, so **merely referencing-and-reaching Newtonsoft emits
`IL2104`/`IL3053` (the assembly rollup) plus concrete `IL2026`/`IL3050`**.
Axis (2) is reached not by serialization but by `JsonSchemaBackend`'s
JSONPath `JToken` scope cursor — out of this ADR's scope (Bounded scope).
Removing `TypeNameHandling` is therefore **necessary but not sufficient** for a
zero-warning core `PublishAot`; this ADR delivers an AOT-clean *typed
extraction/persistence pipeline*, not yet an AOT-warning-free *whole core*.

Why this is worth a major break: three concrete, technical payoffs, not a
strategy bet. (1) **Reflection removal** — the `TypeNameHandling.Auto`
type-by-name resolution was a proven defect class here (ADR 0003/0005), not
just an AOT nuisance; source-gen + explicit converters make the polymorphic
round-trip data-driven and the asymmetry unrepresentable. (2)
**AOT-cleanliness** — the typed pipeline trim/AOT-analyses clean (spike-proven,
zero warnings), so the parts of the codebase a consumer compiles `PublishAot`
no longer carry a structural blocker. (3) **Deploy footprint** — an AOT-clean
serialization core is what lets a consumer ship a self-contained, single-file,
trimmed native binary with no runtime install and lower startup/memory; the
Newtonsoft reflection graph is the single largest thing standing between this
library and that deployment shape. These hold regardless of how the library is
distributed or positioned; they are properties of the code.

The serialization grammar now has one mechanism: **`System.Text.Json`
source-gen** — a `JsonSerializerContext` for the metadata-able bulk, plus four
hand-written `JsonConverter`s for the shapes that are genuinely polymorphic or
collection-shaped:

1. a polymorphic `Schema`/`SchemaElement` converter carrying a `$kind`
   discriminator (the `TypeNameHandling.Auto` replacement *for this hierarchy*);
2. a `PageAction` converter that writes each `object[]` parameter **kind-tagged**
   so its CLR type survives the round-trip;
3. an `ImmutableQueue<LinkPathSelector>` converter (array + `CreateRange` —
   STJ has no AOT-safe built-in for `ImmutableQueue<T>`);
4. `JsonStringEnumConverter<TEnum>` — the AOT-safe generic — for `PageType`,
   `PageActionType`, `DataType`, replacing the Newtonsoft `StringEnumConverter`
   attribute on the enums.

The Schema fold keeps its **one home** (ADR 0002 stands): only its *terminal
projection* changes — `JObject`→`System.Text.Json.Nodes.JsonObject`,
`JArray`→`JsonArray`, `JToken`→`JsonNode`, a 1:1 node-tree swap that preserves
the container/object-list/leaf/value-list branching, the missing-node policy,
the swallow-and-log scope, and the ADR-0002 untyped-leaf divergence (a native
JSON number stays a number, an HTML string stays a string — `JsonValue`
preserves value kind exactly as `JToken.FromObject` did) byte-for-byte. This is
ADR 0002's posture re-applied a third time (after ADR 0003): **additive public
surface, compat shell, staged** — the typed path lands *beside* the `JObject`
path, which is `[Obsolete]`d and removed in a later major, never big-bang.

This **supersedes the serialization grammar of ADR 0002 and ADR 0003** while
preserving their structural results. ADR 0002's result — one fold, backends
supply only document primitives — is untouched; only what the fold *emits*
changes. ADR 0003's result — one keyed blob store, per-payload shells, the
store never sees a config, the uniform missing-value policy — is untouched; the
config **payload shell** still owns serialization and the meaning of *absent*,
it just owns an STJ context instead of a `TypeNameHandling.Auto`
`JsonSerializerSettings`. The keyed blob store seam, its four adapters, and the
cookie shell are unchanged.

Verified, not inferred: a Phase-0 spike compiled the **real, unmodified**
`ScraperConfig` object graph plus a `JsonNode` fold terminal with
`PublishAot=true` for `osx-arm64`, trim/AOT analyzers on and the `IL2026`/
`IL3050` family promoted to errors. It produced a native binary with **zero
trim/AOT warnings** and round-tripped the exact config
`PayloadShellTests.Config_shell_round_trips_selector_chain_and_page_actions`
asserts — including `Convert.ToInt32(Parameters[1]) == 42` over a rematerialised
`object[]` element, the assertion a naive STJ swap fails (see Deliberate
consequences). The typed pipeline's staging strategy is therefore de-risked,
not just designed.

### Discovered constraint (Phase-0 spike, second probe)

The same spike then exercised the JSON backend's actual cursor operations
under AOT — `JToken.Parse`, `SelectTokens`/`SelectToken` (JSONPath),
`DeepClone`, the explicit cast operators, and a `JToken`→`JsonNode` bridge.
Result, on the real osx-arm64 native binary: **the JSONPath subset
functionally works at AOT runtime** (every assertion passed, exit 0) — but the
build emits `IL2104`/`IL3053` (the *Newtonsoft.Json assembly produced trim/AOT
warnings* rollup) and concrete `IL2026`/`IL3050` on
`JObject`/`JToken`/`JValue.GetMetaObject` and `JValue.IConvertible.ToType`→
`JToken.ToObject(Type)`. The first spike pass was clean only because nothing
reached Newtonsoft and the trimmer removed it entirely; once `JsonSchemaBackend`
makes it reachable, the assembly is analyzer-dirty. **Consequence: a
zero-warning core `PublishAot` is not reachable while `JsonSchemaBackend`
retains its Newtonsoft JSONPath cursor — which is the ADR-0002 backend quirk,
out of this ADR's scope (STJ has no JSONPath).** This ADR's deliverable is
re-stated accordingly: an AOT-clean *typed extraction/persistence pipeline*
(Newtonsoft off the public surface, config, schedulers, sinks; the typed
terminal AOT-clean), with full zero-warning core `PublishAot` *gated on a
separate JSON-backend JSONPath migration* (Bounded scope; named follow-up).

## Staging (plan §2.12 — typed path beside `JObject`, never big-bang)

1. **Typed terminal beside the legacy one.** Add the `JsonNode`/typed return
   path; retain the `JObject`-returning `IContentParser` as the legacy seam.
   The fold body is unchanged; only its terminal projection forks.
2. **STJ config/scheduler round-trip.** Replace the config shell's
   `TypeNameHandling.Auto` and `AzureServiceBusScheduler`'s with the source-gen
   context + the four converters; `RedisScheduler`'s `Job` path gets the same
   treatment (Deliberate consequences).
3. **Sinks/formats.** Migrate `IFileSinkFormat`, `BufferedFileSink`,
   `CsvFormat`, `JsonLinesFormat` to the typed/`JsonObject` Row type — the ADR
   0006 **File sink drain** mechanism is untouched, only the **Row format**'s
   row type changes.
4. **`PublishAot` + `InvariantGlobalization`** on the AOT example and an AOT
   smoke test in CI, scoped to the **Newtonsoft-free configuration** (CSS/XPath
   backend + STJ config/schedulers/sinks/typed terminal — the path the spike
   proved zero-warning), and `[Obsolete]` on the `JObject` path. Core itself
   carries `IsAotCompatible`/the analyzers but **not** a zero-warning
   `PublishAot` guarantee while the JSON backend's Newtonsoft JSONPath cursor
   remains (Discovered constraint); that guarantee is the named follow-up.

## Bounded scope — what this does NOT change

- **`JsonSchemaBackend`'s Newtonsoft `JToken` JSONPath cursor.** The JSON
  backend's `TNode` scope cursor is a `Newtonsoft.Json.Linq.JToken` queried
  with JSONPath (`Parse`/`SelectTokens`/`SelectToken`/`DeepClone`). STJ has no
  JSONPath; `JsonNode` cannot select with `$.a.b[*]`. Per ADR 0002 a backend's
  document mechanics are *backend-local quirks*, and per ADR 0007 link
  discovery / a backend's own machinery is explicitly out of a deepening's
  scope. Migrating this cursor off Newtonsoft means choosing a JSONPath
  library for `System.Text.Json` (a new dependency *and* a JSONPath-dialect
  behaviour surface pinned by `JsonParsingTests`) — an ADR-0002-territory
  decision of its own, **the named follow-up that gates zero-warning core
  `PublishAot`** (Discovered constraint). The `JToken`→`JsonNode` bridge lives
  in `JsonSchemaBackend.ExtractRaw` itself (reflection-free, switch on
  `JTokenType`), **not** in the shared fold — so the fold and the typed
  terminal carry zero Newtonsoft reference and AOT-analyse clean on their own
  (the production AOT smoke test proves it). This is ADR 0002 discipline
  applied: a backend's document quirk is backend-local, quarantined at
  `ExtractRaw`, never in the fold. The Newtonsoft reachability is therefore
  contained entirely to `JsonSchemaBackend`; this ADR neither widens nor fixes
  that cursor, exactly the ADR 0004/0005 "preserved verbatim, distinct future
  candidate" posture. Consequence: the *Newtonsoft-free configuration*
  (markup/CSS/XPath backend + STJ config/schedulers/sinks/typed terminal)
  publishes AOT zero-warning today — verified by a committed production AOT
  smoke test (`WebReaper.AotSmokeTest`, wired into CI).
- **The Schema fold's behaviour.** Container/object-list/leaf/value-list
  branching, typed coercion grammar, the missing-node→empty policy, the
  swallow-and-log scope, and the deliberate HTML-string-vs-JSON-native
  untyped-leaf divergence (ADR 0002, pinned by `SchemaFoldTests`) are
  byte-identical. Only the emitted node type changes. A `JToken.DeepEquals`
  assertion becomes a `JsonNode.DeepEquals` assertion; the *shape* it checks is
  the same.
- **ADR 0001/0004/0005/0006 mechanisms.** `CrawlOutcome` stays a closed
  three-arm sum; the one `IPageLoader`/`IPageLoadTransport` seam is untouched;
  `RedisConnectionPool` is untouched (this changes what `RedisScheduler`
  *serialises*, not how it *connects*); the buffered-drain mechanism is
  untouched (only the Row type changes).
- **The keyed blob store seam.** Still an opaque UTF-8 `string` under a key,
  `null` ⇔ absent, four adapters. STJ produces the string the shell stores; the
  store still never knows which payload it holds.
- **The cookie payload shell (`CookieStore`).** ADR 0003 introduced the config
  *and* cookie payload shells as a pair; §2.12 names only the config shell and
  the schedulers. `CookieStore` still serialises its `CookieContainer` quirk
  with Newtonsoft and is **preserved verbatim** here — its `System.Net`
  cookie-mapping quirk is a distinct, smaller migration (no polymorphism, no
  `TypeNameHandling`), a named sibling follow-up alongside the JSON-backend
  cursor, not silently in scope. Same posture as ADR 0005 naming the then-unfixed
  `RedisScheduler` rather than widening its candidate.
- **Relicensing (ADR 0017) and the `[ScrapeSchema]` source generator (ADR
  0010).** Out of scope here. ADR 0008 is the AOT-clean serialization
  substrate; ADR 0010's reflection-free typed materialiser is built *on* the
  source-gen context this ADR introduces, later.
- **The Cosmos sink's native interop.** The spike showed the Cosmos SDK drags
  `Microsoft.Azure.Cosmos.ServiceInterop.dll`/`vcruntime140*.dll` into an AOT
  publish. It did not break the config/fold AOT path this ADR's gate proved;
  whether `CosmosSink` itself is AOT-publishable or is documented as an
  optional non-AOT sink is the staging-step-4 full-core `PublishAot`
  question, deliberately not pre-decided here (the ADR 0001/0002/0003
  scope-discipline).
- **`BrowserPageLoadTransport`'s `Assembly.Location` (IL3000).** Turning on
  `IsAotCompatible` on core surfaced a *single-file* finding independent of the
  Newtonsoft axis: the Puppeteer browser transport reads
  `Assembly.Location`, which is empty under single-file/AOT publish. Like the
  Cosmos sink, the heavyweight browser path is its own AOT concern, named here
  and left for a dedicated follow-up, not silently in scope.
- **Build-time analysis is narrower than publish-time ILC.**
  `IsAotCompatible`'s Roslyn analyzers flag only APIs *annotated*
  `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` — so a core `dotnet
  build` flags `CookieStore`'s `JsonConvert` and the browser
  `Assembly.Location`, but **not** Newtonsoft's `JToken`/`JObject` DOM
  (JSONPath cursor, `CosmosSink`, the `[Obsolete]` shim), whose AOT-dirtiness
  is the *ILC publish-time* `IL2104`/`IL3053` whole-assembly rollup the spike
  proved. A clean core `dotnet build` is therefore necessary, not sufficient,
  evidence; the `WebReaper.AotSmokeTest` `dotnet publish` job is the
  authoritative zero-warning check, on the Newtonsoft-free path only.

## Deliberate consequences (bugs closed by construction — see CONTEXT.md "Flagged ambiguities")

- **The ADR 0005 `RedisScheduler` `Job` round-trip asymmetry is closed.** ADR
  0005 preserved it verbatim and named it a distinct future candidate; this is
  that candidate. `RedisScheduler` serialises and deserialises a `Job` through
  the *same* STJ context + converters as the config path, so its
  `ImmutableQueue<LinkPathSelector>` and `PageAction.Parameters` round-trip with
  type fidelity. The asymmetry is now *unrepresentable*: there is no
  `TypeNameHandling` knob to set differently on the two sides.
- **The `object[]`→`JsonElement` hazard never arises.** STJ's default for a
  bare `object` rematerialises a `JsonElement`, on which
  `Convert.ToInt32` throws — a silent break of the existing
  `PayloadShellTests` `Parameters[1] == 42` contract. The `PageAction`
  converter writes each parameter kind-tagged and reads it back to its CLR type
  (`string`/`int`/`bool`/`double`/null), so `Convert.ToInt32` keeps working and
  `PayloadShellTests` stays green **unmodified**. This is the same defect class
  ADR 0003/0005 fought (polymorphic members losing their type across a
  serializer boundary), closed for good rather than relocated.
- **`Schema`'s `ICollection<SchemaElement>` no longer self-erases.** `Schema`
  is both the container node *and* an `ICollection<SchemaElement>`. STJ detects
  the collection interface and would serialise a `Schema` as a bare JSON array,
  silently dropping `Field`/`Selector`/`Children` — a new latent bug a naive
  STJ swap introduces. The explicit `JsonConverter<Schema>` keeps it an object;
  STJ's collection detection never runs for that type. The public `ICollection`
  API of `Schema` is unchanged — the fix is in the converter, not the type.

## SemVer

**Major.** The public `JObject` surface changes: `IContentParser.ParseAsync`'s
return type, `ParsedData.Data`, `IFileSinkFormat.Header`/`FormatRow`, and
`PostProcess(Func<Metadata, JObject, Task>)` on `ScraperEngineBuilder` and
`SpiderBuilder`. Staging means the break is *announced* — the typed path ships
additively and the `JObject` path is `[Obsolete]`d with a deprecation window —
but the eventual removal of the `JObject` surface is breaking. Called out loud,
precedent ADR 0004 (the one-page-loader major): a deliberate, SemVer-flagged
break isolated behind a compat shell, not a silent regression.

## Considered options

- **STJ source-gen + four converters, typed terminal beside `JObject`, staged
  (chosen).** The only mechanism that is AOT-clean (spike-proven, zero trim
  warnings on the real graph), preserves the ADR 0002/0003 structural results,
  closes the ADR 0005 asymmetry as a side-effect rather than a separate
  candidate, and keeps the source-gen context ADR 0010 will extend. Staging
  bounds the major break behind a compat shell.
- **Big-bang `JObject`→typed swap (rejected).** Changes ~21 files and the
  public surface in one breaking move with no deprecation window; contradicts
  the ADR 0002/0003 "additive surface + compat shell" posture the project has
  applied three times; nothing to fall back to if a consumer's `JObject`
  `PostProcess` callback breaks.
- **STJ *reflection-based* (no source generator) (rejected).** Smaller diff,
  but `JsonSerializer.Serialize(object, options)` without a resolver is itself
  `IL2026`/`IL3050` — it removes Newtonsoft but **not** the AOT blocker, so the
  load-bearing reason for the work is unmet. The spike deliberately used the
  source-gen context to prove the clean path.
- **Keep Newtonsoft, drop only `TypeNameHandling` (rejected).** Newtonsoft is
  reflection-based and AOT-hostile independent of `TypeNameHandling`; and
  dropping `TypeNameHandling` without a polymorphic replacement re-breaks
  exactly the `PageAction.Parameters`/selector-chain round-trip ADR 0003 fixed.
  Strictly worse on both axes.
- **STJ `JsonNode`-only, no source-gen context (rejected).** The `JsonNode`
  terminal alone would serve the fold, but the config/scheduler polymorphic
  round-trip still needs typed metadata, and ADR 0010's reflection-free
  `[ScrapeSchema]` materialiser needs a `JsonSerializerContext` to extend.
  Choosing `JsonNode`-only now would force re-introducing the context later —
  indirection deferred, not avoided.
