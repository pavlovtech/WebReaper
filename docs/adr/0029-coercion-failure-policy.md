# The Schema fold's per-leaf swallow-and-log policy is the documented contract; coercion failures are pinned, log messages differentiated

## Status

**Accepted — implementation complete** (2026-05-20; landed on branch
`adr-0029-coercion-failure-policy` off `origin/master` 5c4e1d0,
awaiting merge — fourth and final slice of the fresh
`/improve-codebase-architecture` review wave, after ADR-0026 / PR #82,
ADR-0027 / PR #83, and ADR-0028 / PR #84). No behaviour change at the
contract surface — same leaves stay unset, no extra exceptions
propagate. The only observable delta is the *log message text*: a
coercion failure logs as *coercion failed*, an overflow as *coercion
overflowed*, an unexpected exception as *unexpected error extracting
field*. Folds naturally into whatever release the user batches next;
no SemVer impact.

## Context

The **Schema fold** ([WebReaper/Core/Parser/Concrete/SchemaContentParser.cs:99-106](../../WebReaper/Core/Parser/Concrete/SchemaContentParser.cs#L99-L106))
wraps every per-leaf assignment in a single `try { … } catch (Exception
ex) { _logger.LogError(ex, "Error during parsing phase"); }`. The
catch is unconditional and the log message is generic — the policy is
*swallow every exception during leaf extraction, log at error level,
leave the field unset*. The behaviour is genuinely deliberate: a Schema
authored against a noisy page should not abort the whole crawl just
because one field's value is malformed, and the per-page sink should
still receive whatever else parsed cleanly.

But the policy is *emergent*, not pinned:

- **No test exercises the coercion-failure path.** Every typed test in
  `TypedFoldTests` and `JsonParsingTests` uses well-formed input —
  `"7"` parses to `7`, `"true"` parses to `true`. The behaviour of a
  `DataType.Integer` field meeting the string `"abc"` is undocumented
  *by test* (today: caught by the leaf try, logged, field unset).
- **A user with malformed data and a user with a missing-selector see
  the same outcome and the same generic "Error during parsing phase"
  log line.** Coercion failure (`FormatException` / `OverflowException`
  from `int.Parse` / `float.Parse` / `bool.Parse` / `DateTime.Parse`)
  is indistinguishable from a malformed-selector exception, a backend
  bug, or any other defect in the same line. The user cannot tell from
  the log whether the field was missing or whether the value didn't
  parse — and silently-empty fields *look the same in the output JSON*
  as missing-selector fields (`JsonValue.Create(string.Empty)` for a
  single value, never-assigned for a typed leaf).
- **The catch is over-broad.** `catch (Exception)` covers every
  conceivable failure including bugs (NullReferenceException from a
  backend regression, OutOfMemoryException, …), which the policy
  documents *only by accident*. A future contributor reading the
  catch cannot tell whether the breadth is deliberate or sloppy.

Measured against LANGUAGE.md:

- **The interface is the test surface, but the test surface is
  silent.** The "swallow-and-log" policy IS the contract a Schema
  author needs to understand, and *no test exercises it*. A future
  reviewer asking "should we throw on coercion failure?" has nothing
  pinned to push back against.
- **Two failure shapes, one log message.** Coercion failure and
  backend failure are different *causes* a user might want to address
  differently (fix the page's data vs fix the selector vs file a
  backend bug). Identical telemetry conflates them.

The deepening is *not* a behaviour change — that would be a
breaking-and-undesirable shift (silent partial parses are the right
default for a noisy-page scraper, not throw-on-first-bad-value). The
deepening is: **make the policy a documented contract** with a test
that pins it, and **distinguish coercion failures from other failures
in the log** so observability matches the actual failure mode.

## Decision

Pin the existing policy as deliberate; differentiate the log messages
along the natural exception-type axis; add tests at the construction
interface.

1. **Split the per-leaf catch in `FillOutput`** ([SchemaContentParser.cs:99-106](../../WebReaper/Core/Parser/Concrete/SchemaContentParser.cs#L99-L106))
   into three arms, ordered most-specific first:

   ```csharp
   try
   {
       result[item.Field] = item.IsList
           ? GetValueList(scope, item)
           : GetSingleValue(scope, item);
   }
   catch (FormatException ex)
   {
       _logger.LogError(ex,
           "Coercion to {Type} failed for field '{Field}' (raw input could " +
           "not be parsed); field left unset",
           item.Type, item.Field);
   }
   catch (OverflowException ex)
   {
       _logger.LogError(ex,
           "Coercion to {Type} overflowed for field '{Field}' (raw input " +
           "out of range for the target type); field left unset",
           item.Type, item.Field);
   }
   catch (Exception ex)
   {
       _logger.LogError(ex,
           "Unexpected error extracting field '{Field}' (selector " +
           "'{Selector}'); field left unset",
           item.Field, item.Selector);
   }
   ```

   The third arm preserves the *exact* current catch-all breadth — no
   behaviour change. The two specific arms are what `Coerce` actually
   throws today (`int.Parse` / `float.Parse` / `bool.Parse` /
   `DateTime.Parse` throw `FormatException`; `int.Parse("99999999999")`
   for `DataType.Integer` throws `OverflowException`).

2. **Add unit tests pinning the policy** in a new
   `TypedCoercionFailureTests.cs` (companion to `TypedFoldTests`):

   - `DataType.Integer` meets `"abc"` → field unset, no throw.
   - `DataType.Integer` meets `"99999999999"` (overflow) → field unset,
     no throw.
   - `DataType.Float` meets `"xyz"` → field unset.
   - `DataType.Boolean` meets `"neither"` → field unset.
   - `DataType.DataTime` meets `"garbage"` → field unset.
   - A typed leaf-list (`IsList = true`) with one malformed element
     among well-formed ones: today the whole list is dropped (one bad
     element trips the per-leaf catch). Pin this as the current,
     deliberate behaviour — alternatives are listed in *Considered
     options*.
   - Coercion failure logs at `LogLevel.Error` with a message
     containing the field name (pinned via a capturing logger; the
     differentiated-message decision becomes a tested contract, not
     just a code comment).

3. **Document the policy in the Schema glossary**: a new
   *Coercion-failure handling* paragraph under the **Typed coercion**
   entry in `CONTEXT.md`.

4. **A "Flagged ambiguities" bullet** records the decision and the
   rejected alternatives so future reviews don't re-suggest them.

The change is internal-only on behaviour (the policy is identical at
the contract surface); the only observable shift is the log message
text and the addition of pinning tests.

## Considered options

### (a) Throw on coercion failure (strict mode by default)

`Coerce` propagates `FormatException` / `OverflowException` instead of
the fold swallowing them. Rejected: silently-noisy pages are the
common case for a web scraper; aborting an entire page because one
field's number is "—" or "n/a" turns a usable partial result into a
total failure. The existing "drop the bad field, keep the rest"
policy is the right default for the domain. (For the rare consumer
who wants strict-mode, see option (b).)

### (b) Add a `SchemaErrorPolicy` knob (Swallow / Throw / SwallowSingleArmDropList)

A configurable enum on `Schema` or on the parser. Rejected as
scope-creep for this ADR: no consumer has asked for strict mode; one
adapter (today's *swallow*) does not justify a seam by LANGUAGE.md's
*"two adapters means a real seam"*. If a real second variation
emerges, the knob is a clean follow-up — naming the policy as a
documented contract here is the prerequisite either way.

### (c) Differentiate coercion failures from backend failures by *output shape*

Emit a typed error marker into the JSON output (e.g.,
`{"__error": "FormatException", "raw": "abc"}` instead of an unset
field) so downstream sinks can distinguish "missing" from "malformed."
Rejected: a breaking output-shape change for every downstream sink
(JSON-lines / CSV / Mongo / Redis / Cosmos) for an observability gain
that the log-level differentiation in this ADR already addresses for
operators. Output-shape changes for parse-error semantics are a
bigger refactor; pinning the *log* policy first is the deepening
worth in scope.

### (d) Drop the catch-all entirely; only `FormatException` /
`OverflowException` are swallowed

Narrowing to just the two expected exception types means a backend
bug (e.g., a malformed `IElement`-cast in a custom backend) would
propagate and abort the page. Rejected: that *is* a breaking
behaviour change at the contract surface — today, backend bugs are
silently absorbed, and there is no test asserting the contrary.
Without a second adapter wanting strict-bug-throw, narrowing the
catch is the "delete pass-through" trap LANGUAGE.md warns about —
complexity *would not* reappear, but the swallow IS load-bearing for
"a noisy page does not abort the crawl" robustness, and we have no
test or consumer feedback that throws are desired in that case. Keep
the catch-all third arm; differentiate the message.

### (e) Drop the bad-element silently from a typed leaf-list instead of dropping the whole list

Today: `IsList = true` + `DataType.Integer` + one malformed element →
the whole `JsonArray` is unassigned (the catch wraps the *whole*
list-build, not each element). A different policy: catch per element,
emit the array with the malformed element dropped. Rejected for this
ADR's scope: it *is* a behaviour change at the contract surface (the
shape of the partial result differs); the per-element catch belongs
in a follow-up ADR if the user signals demand. Pin the current
whole-list-drop as deliberate so a future review doesn't accidentally
"fix" it without thought.

## Consequences

- **The swallow-and-log policy is a tested contract**, not an
  emergent property of a generic catch. A future contributor reading
  the catch sees three commented arms and a test file naming the
  rejected alternatives.
- **Coercion failures are visible in the log**, not conflated with
  backend or selector failures. Operators triaging "field missing"
  alerts can distinguish *page had bad data* from *selector is wrong*
  without re-reading the stack trace.
- **Test surface widens by six tests** pinning the policy across
  every `DataType` arm plus the typed-leaf-list whole-list-drop
  property.
- **No SemVer impact.** Internal-only refactor of the catch; same
  observable behaviour (same fields stay unset, no extra exceptions
  propagate). The log message text changes — that is observability
  metadata, not contract.
- **CONTEXT.md grows by one entry** under **Typed coercion**
  documenting the policy, and one "Flagged ambiguities" bullet
  pinning the five rejected paths above.

## Implementation status

All four planned changes landed in one commit on
`adr-0029-coercion-failure-policy`:

1. ✅ `WebReaper/Core/Parser/Concrete/SchemaContentParser.cs` —
   `FillOutput`'s per-leaf catch is now three arms (most-specific
   first): `FormatException` → "Coercion to {Type} failed for field
   '{Field}' …", `OverflowException` → "Coercion to {Type} overflowed
   …", `Exception` (catch-all third arm) → "Unexpected error
   extracting field '{Field}' (selector '{Selector}') …". A block
   comment above the try documents the policy as deliberate and names
   the load-bearing reason ("a noisy page must not abort the crawl").
2. ✅ `WebReaper.Tests/WebReaper.UnitTests/CapturingLogger.cs` (new)
   — small internal `ILogger` that captures each Log call as a
   `(LogLevel, message, exception)` tuple so a test can assert the
   coercion-failure message structure.
3. ✅ `WebReaper.Tests/WebReaper.UnitTests/TypedCoercionFailureTests.cs`
   (new) — seven tests pinning the policy: Integer + non-numeric
   text → unset + FormatException + "Coercion to Integer failed"
   message; Integer + overflow value → unset + OverflowException +
   "Coercion to Integer overflowed"; Float / Boolean / DataTime + bad
   text variants; typed leaf-list with one malformed element drops
   the whole list (documented as deliberate, per-element drop is a
   distinct follow-up); a happy-path sanity test asserting no error
   is logged on a well-formed value.
4. ✅ `CONTEXT.md` — the **Typed coercion** glossary entry gains a
   *Coercion-failure handling* paragraph naming the ADR-0029 policy
   and the pinning tests; one new "Flagged ambiguities" bullet
   captures the decision and the five rejected paths (strict throw /
   `SchemaErrorPolicy` knob / typed error marker / narrow catch /
   per-element drop) so future reviews don't re-suggest them.

### Guardrails (run on the branch tip)

- `dotnet build WebReaper.sln` — **0 errors**; the only new warning
  was an `xUnit2029` (`Assert.Empty(...Where(...))` should be
  `Assert.DoesNotContain`), which is fixed.
  `WarningsAsErrors=CS1591` on core therefore green — the
  ADR-0029-touched surface stays Tier-2 internal (the fold; no
  documented-contract surface change).
- `dotnet test WebReaper.sln --no-build` (non-Integration) —
  **120/120 pass**: 101 unit (94 pre-0029 + 7 new
  `TypedCoercionFailureTests`) + 10 Sqlite + 4 Puppeteer + 3 Mongo +
  1 Cosmos + 1 AzureServiceBus. No pre-existing test failed; the
  only behaviour the new tests pin is the swallow-and-log policy,
  which was already the runtime contract (the previous tests just
  never exercised it).
- Live-site `WebReaper.IntegrationTests` not run on the branch —
  they hit `alexpavlov.dev` with real Puppeteer/Chromium and
  `Task.Delay` up to 25 s, slow and environmentally flaky; the CI
  workflow runs them on the PR.
