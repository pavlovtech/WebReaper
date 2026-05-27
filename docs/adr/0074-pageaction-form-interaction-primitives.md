# `PageAction` form-interaction primitives: `Fill`, `Press`, `ScrollIntoView`

## Status

**Proposed (draft).** Three new sealed-record arms on the [ADR-0035](0035-pageaction-closed-sum.md)
closed sum. Additive (no existing arm changes shape, no deprecation), SemVer-minor.
Implementation pending the ADR-0060 arm-local tool projection pattern (PR #134)
landing on master; see [References](#references).

## Context

[ADR-0035](0035-pageaction-closed-sum.md) made `PageAction` a closed sum of seven typed arms:
`Click`, `Wait`, `ScrollToEnd`, `EvaluateExpression`, `WaitForSelector`,
`WaitForNetworkIdle`, `SemanticAct`. The arm set covers reading, waiting,
and navigation triggers, but **none of the seven mutates form state**. A
real dynamic page is `<input>` + `<button>` + `<select>` plus a JavaScript
framework; today a WebReaper consumer who needs to fill a search field
and submit it has three options, all bad:

1. **`EvaluateExpression(jsExpression)`** with a hand-rolled JS payload
   that sets `.value = '...'` directly. Silently fails on React / Vue /
   Svelte controlled components because the framework's `_valueTracker`
   bypasses changes that don't go through the native property setter.
2. **`SemanticAct("type 'cats' in search box")`** routed to the LLM
   resolver (ADR-0050). The resolver returns one of the six concrete
   arms (none of which can fill a field), so it falls back to an
   `EvaluateExpression` with the same silent-failure mode, or returns
   `null` and throws `SemanticActResolutionException`.
3. **External Puppeteer / Playwright code wrapping the WebReaper
   builder.** The consumer abandons the declarative DSL for one step;
   the persisted `ScraperConfig` no longer round-trips the workflow.

The gap is *structural*, not a missing feature. Firecrawl's `/scrape`
API exposes nine action types; the three WebReaper lacks are
`write(text)`, `press(key)`, and `scroll(direction, selector?)`. The
[[webreaper-monetization-strategy]] funnel needs OSS feature-parity
with Firecrawl *plus* structural differentiators (`WaitForNetworkIdle`
and `SemanticAct`, today). Without form-interaction primitives, the
parity claim has a visible hole every prospect spots on read-one of
the README.

| Firecrawl action | WebReaper today | Gap? |
|---|---|---|
| `wait(milliseconds OR selector)` | `Wait(ms)` + `WaitForSelector(selector, timeoutMs)` | WebReaper more expressive (configurable timeout) |
| `click(selector, all)` | `Click(selector)` | Minor: missing `all` (click-all-matches); out of this ADR's scope |
| `write(text)` | none | **REAL GAP**: `Fill` closes it |
| `press(key)` | none | **REAL GAP**: `Press` closes it |
| `scroll(direction, selector?)` | `ScrollToEnd` only | **REAL GAP**: `ScrollIntoView` closes the element-scroll case; the page-direction case stays out of scope |
| `executeJavascript(script)` | `EvaluateExpression(expr)` | match |
| `screenshot` | none | NOT a gap (observation, different category) |
| `pdf` | none | NOT a gap (observation) |
| `scrape` | none | NOT a gap (the agent driver, ADR-0051, handles it) |
| (none) | `WaitForNetworkIdle` | **WebReaper-only** (ADR-0057), kept |
| (none) | `SemanticAct(intent)` | **WebReaper-only** (ADR-0050), kept |

After this ADR: WebReaper has 10 mutation arms (the existing 7 + 3 new).
The marketing slice is unchanged from ADR-0050's framing: "same
vocabulary as Firecrawl, plus `WaitForNetworkIdle` and `SemanticAct`
they don't have." The "same vocabulary" half stops being a polite
overstatement.

The `SemanticAct` resolver also gains: today the LLM whitelist offers
four concrete shapes (`click`, `wait`, `waitFor`, `evaluate`); none can
fill a field, so an intent like `"fill in the search box with cats"`
resolves to a sub-ideal `click(input)` (with no follow-on type-in
action) or `null` (resolution failure). With `Fill` / `Press` /
`ScrollIntoView` in the closed sum, the same intent resolves cleanly
to `Fill(input-selector, "cats")`; see [§ SemanticAct interaction](#semanticact-interaction).

## Decision

Three new sealed-record arms on the existing closed sum. No changes
to the seven existing arms; no deprecation.

### 1. `PageAction.Fill(string Selector, string Value)`: text input

```csharp
public sealed record Fill(string Selector, string Value) : PageAction;
```

Fills the matched element with `Value`. **Implicit policy:**

- **Auto-wait, 30 s default.** The selector is resolved against the page
  with a per-poll wait (CDP) or native auto-wait (Playwright). A
  selector that never appears throws `TimeoutException`. The 30 s
  default matches Playwright's `page.FillAsync` and Firecrawl's `wait`
  default; the choice is policy, not a record field (see [Considered
  options (i)](#i-auto-wait-as-an-explicit-timeoutms-field-on-fill)).
- **Element-shape check.** The matched element must be an
  `HTMLInputElement`, `HTMLTextAreaElement`, or `isContentEditable`.
  Anything else throws (no silent-no-op, the Firecrawl `write(text)`
  failure mode this ADR rejects; see [Considered options (d)](#d-firecrawls-writetext-implicit-focus-shape)).
- **Disabled check.** A disabled matched element throws.
- **Clear-before-fill.** Matches `page.FillAsync` semantics; if a
  caller wants append, they read the current value via
  `EvaluateExpression` first.
- **Framework-observed events.** The CDP transport uses the
  React-friendly native-setter trick (the property descriptor's
  `value` setter, not `.value = X` directly), then dispatches `focus`,
  `input`, `change` synchronously. Playwright's `page.FillAsync` does
  the equivalent natively. Controlled components in React / Vue /
  Svelte observe the change (the silent-failure mode of the
  `EvaluateExpression` escape hatch is closed by construction).
- **No blur.** Matches Playwright. A caller wanting blur dispatches
  `Press("Tab")` next.

### 2. `PageAction.Press(string Key)`: keyboard event

```csharp
public sealed record Press(string Key) : PageAction;
```

Dispatches a keyboard event on the **currently-focused element** (no
selector field; see [Considered options (f)](#f-presskey-selector-optional-target)).

- **Key vocabulary: Playwright's format.** Single printable chars
  (`"a"`, `"A"`, `"1"`); named keys (`"Enter"`, `"Tab"`, `"Escape"`,
  `"ArrowDown"`, `"ArrowUp"`, `"ArrowLeft"`, `"ArrowRight"`,
  `"Backspace"`, `"Delete"`, `"Home"`, `"End"`, `"PageUp"`,
  `"PageDown"`, function keys `"F1"`-`"F12"`); modifier combos
  (`"Control+A"`, `"Shift+Tab"`, `"Meta+C"`). The transport translates;
  the closed-sum contract is the string format.
- **Cross-transport mapping.** Playwright accepts the format directly
  (`page.Keyboard.PressAsync(key)`). CDP's `Input.dispatchKeyEvent`
  takes a `key` + `code` + `windowsVirtualKeyCode` + `modifiers`
  bitmask; the transport carries a static `key-string → CDP-fields`
  map covering printable + named + modifier-prefixed keys (~80 entries
  total; the keyboard layout is finite). An unknown key string throws
  `ArgumentException` with the offending string in the message; the
  closed-sum-default-arm-throws pattern (ADR-0035) one level deeper.
- **No target field.** If a caller needs to press a key against a
  specific element, they `Click(sel) + Press(key)` (or `Fill(sel,
  value) + Press("Enter")`); the focused-element contract matches
  Playwright + Firecrawl.

### 3. `PageAction.ScrollIntoView(string Selector)`: element scroll

```csharp
public sealed record ScrollIntoView(string Selector) : PageAction;
```

Scrolls the matched element into the viewport.

- **Auto-wait, 30 s default.** Same implicit policy as `Fill`; the
  CDP transport reuses the existing `CdpPageActionDispatcher.WaitForSelectorAsync`
  helper ([ADR-0057](0057-cdp-network-idle.md)), Playwright's
  `Locator.ScrollIntoViewIfNeededAsync` auto-waits natively.
- **Distinct from `ScrollToEnd`.** Two different intents, two
  different arms ([Considered options (c)](#c-firecrawl-shape-scrolldirection-selector-fill--press)).
  `ScrollToEnd` triggers infinite-scroll loading at the page level.
  `ScrollIntoView(sel)` brings a specific element into the viewport:
  the dominant use case for "click an item in a virtualized list."

### Wire format (additive)

Three new arm tags in the codec ([WebReaper/Serialization/Converters/PageActionJsonConverter.cs](../../WebReaper/Serialization/Converters/PageActionJsonConverter.cs)):

```jsonc
{ "type": "fill",         "selector": "<css>", "value": "<text>" }
{ "type": "press",        "key": "<key>" }
{ "type": "scrollIntoView", "selector": "<css>" }
```

Old (pre-v10.1) configs round-trip unchanged. New configs deserialised
by an old reader throw `JsonException("unknown PageAction type '<tag>'")`,
the closed-sum invariant (acceptable per SemVer's backward-compat-only
mandate; documented in [§ SemVer](#semver)).

### Builder surface

Three new fluent methods on [PageActionBuilder](../../WebReaper/Builders/PageActionBuilder.cs)
mirroring `Click` / `WaitForSelector`:

```csharp
public PageActionBuilder Fill(string selector, string value);
public PageActionBuilder Press(string key);
public PageActionBuilder ScrollIntoView(string selector);
```

Each uses `ArgumentException.ThrowIfNullOrWhiteSpace` on its string
arguments (sibling to the existing builder's validation).

### Tool-calling registry (ADR-0060)

Each new arm is one nested static class in `WebReaper.AI.Tools.PageActionTools`
(the post-PR #134 arm-local pattern):

```csharp
internal static partial class PageActionTools
{
    internal static class Fill
    {
        public const string Name = "ActFill";
        public static readonly AIFunction Descriptor = /* JSON Schema */;
        public static ToolCallResult<PageAction.Fill> FromArguments(JsonElement args);
    }
    internal static class Press { /* … */ }
    internal static class ScrollIntoView { /* … */ }
}
```

Both registries grow uniformly: the brain's `ForBrain()` list adds
three flat entries (10 → 13); the resolver's `ForResolver()` list adds
three (6 → 9). Per ADR-0060 fork 8, the resolver's
`ActSemanticAct` absence is preserved; no SemanticAct loop is
representable in the new tool set, structurally.

The LLM action resolver's whitelist prompt extends from four shapes
(`click` / `wait` / `waitFor` / `evaluate`) to seven (`+ fill / press
/ scrollIntoView`); consumer-authored resolvers carry their own
prompt and remain free-form.

### SemanticAct interaction

`SemanticAct(intent)` (ADR-0050) resolves an intent to **one** concrete
arm and caches it per crawl. With the three new arms in the resolver's
registry:

- `SemanticAct("click sign in")` → `Click(sign-in-selector)` (unchanged).
- `SemanticAct("fill in the search box with cats")` → `Fill(input-selector, "cats")` (newly possible; was sub-ideal or null before).
- `SemanticAct("press enter")` → `Press("Enter")` (newly possible).
- `SemanticAct("scroll the next product into view")` → `ScrollIntoView(product-selector)` (newly possible).

**Multi-step intents** (`"fill the box with cats and press Enter"`)
still resolve to **one arm** in v1; the LLM is instructed in the
satellite's prompt to pick the most structural single arm and rely
on a subsequent `SemanticAct` invocation for the next step (or the
caller composes `SemanticAct(fill) + Press("Enter")` explicitly).
The `Sequence(arm1, arm2, ...)` composite arm that would unblock
multi-arm resolution stays deferred; see [Considered options (h)](#h-sequencearm1-arm2--composite-arm-in-v1).

## Considered options

### (a) Three arms; `Fill` + `Press` + `ScrollIntoView` (chosen)

Closes the three real gaps (`write`, `press`, the element half of
`scroll`); leaves the page-direction half of Firecrawl's `scroll` out
on no-observed-caller grounds; each arm has exactly one intent and one
shape. ADR-0035's closed-sum discipline preserved.

### (b) Two arms; `Fill` + `Press` only

Defer `ScrollIntoView` until a caller surfaces. **Rejected.**
Element-into-view-scroll is the dominant ergonomics for virtualised
SPA lists ("scroll to find the row, click it"); the cost is one
five-line `sealed record` plus two switch arms and a tool descriptor.
The same cost-vs-value math the ADR-0050 deferral list ran against
v2 features comes out the other way: this one shipping is cheap and
the use case is the common case for AI agents driving long pages.

### (c) Firecrawl-shape `Scroll(ScrollDirection, string? selector)` + `Fill` + `Press`

The Firecrawl-vocabulary-parity option. **Rejected.** One arm,
`Scroll(direction, selector?)`, conflates two operations whose
behaviours don't share a parameter:

- `(Down, null)` → `window.scrollTo(0, document.body.scrollHeight)` (page scroll).
- `(Up, null)` → `window.scrollTo(0, 0)`.
- `(Down, "selector")` → `element.scrollIntoView()`. The `Down` is
  vestigial; `scrollIntoView` has no direction parameter.
- `(Up, "selector")` → `element.scrollIntoView()`. Same vestigial
  `Up`. `(Down, "selector")` and `(Up, "selector")` are observably
  identical.

The `direction` field is load-bearing when `selector` is null and
vestigial when it is set. The `selector` field is null when scrolling
the page and required when scrolling an element. One arm where the
fields' meanings depend on what *other* fields are set is the exact
shape ADR-0035 set out to kill, just one level deeper than the old
`(PageActionType, object[])`; the closed-sum codec layer dispatches
on `selector == null` vs `selector != null`, a hidden second
discriminator.

Two arms (`ScrollToEnd` unchanged + new `ScrollIntoView(sel)`) gives
each operation one shape, one dispatch path, no hidden discriminator.
The page-up case (`Scroll(Up)`) is dropped; no observed caller, and
adding it later is one additional `sealed record` per ADR-0035's
extension pattern.

### (d) Firecrawl's `write(text)` implicit-focus shape

Firecrawl's `write` takes a `text` field only; focus is implicit from
the prior `click(selector)`. **Rejected.** If the prior click hits a
`<div>` rather than an `<input>` (an interactive parent element, the
common React layout pattern), the `<div>` receives focus but
`document.activeElement` is now a non-text-input target; the
subsequent `write` silently does nothing on most browsers. The
failure mode is "the crawl ran, the records are empty, no error in
the logs." Selector-explicit `Fill(selector, value)` rules this out
by construction: the element resolution and shape check throw at
dispatch time on a wrong-shape target.

### (e) `Fill(selector, value, FillOptions options)` with configurable behaviour

A speculative `FillOptions` record bundling `Clear` (bool, default
true), `BlurAfter` (bool, default false), `TimeoutMs` (int, default
30_000), `FireChangeEvent` (bool, default true). **Rejected.**
Speculative-API anti-pattern: no consumer has asked for clear-vs-append,
non-default timeout, or blur-on-completion. If demand surfaces:

- Append → a separate `AppendValue(selector, suffix)` arm, or
  composition (`EvaluateExpression("…")` reading current value first).
- Custom timeout → `WaitForSelector(sel, custom_timeout) + Fill(sel, value)`.
- Blur-after → `Press("Tab")` follow-on.

Each is a one-line composition. Adding an options record to every
arm-with-implicit-policy is the road to the `(PageActionType, object[])`
shape the closed sum already escaped.

### (f) `Press(key, selector?)`; optional target

Make `Press` carry an optional selector that gets clicked-and-focused
before the key event. **Rejected.** Two reasons:

1. **Focused-element-only matches every peer system.** Playwright's
   `Keyboard.PressAsync` takes only a key; Firecrawl's `press`
   takes only a key. Diverging here would have to be motivated by a
   use case that's natively expressed neither way; no such case
   surfaced.
2. **Implicit focus management is the wrong place to put the
   complexity.** A caller wanting "press Enter on the search box" is
   one composition: `Click(sel)` (focus) `+ Press("Enter")`. Putting
   the focus inside `Press` re-introduces the implicit-focus
   fragility that disqualified `write(text)` ([§ (d)](#d-firecrawls-writetext-implicit-focus-shape)).

### (g) Deprecate `ScrollToEnd` to a `Scroll(Down, null)` alias

Add the conflated `Scroll(direction, selector?)` arm anyway and mark
`ScrollToEnd` with `[Obsolete]`. **Rejected.** No user benefit; the
existing arm name describes exactly its dominant use case ("scroll
to end" is what users *say*); `[Obsolete]` emits a compile warning
on every existing call site for no payoff. The closed-sum-widening
discipline ADR-0035 set is **prefer adding arms over reshaping the
existing ones**; this option violates it for tax purposes (taxonomic
neatness, not semantic improvement).

### (h) `Sequence(arm1, arm2, …)` composite arm in v1

A new sealed-record arm carrying a list of other arms; the transport
dispatches them in order; the LLM resolver can return a multi-arm
chain for a multi-step intent. **Rejected for v1, deferred to v2.**
The capability is real (`SemanticAct("fill 'cats' and press Enter")`
naturally resolves to two arms), but adding `Sequence` raises three
questions none has an obviously-right answer for:

1. **Cache-key shape (ADR-0050).** Does the resolver cache the whole
   sequence per intent, or each constituent arm? The latter is fine
   if every arm in the sequence is stable across pages; the former
   if some arm depends on a prior arm's effect (e.g. the second
   arm's selector resolves only after the first arm dispatches).
2. **Recursion safety.** Can a `Sequence` contain a `Sequence`? A
   `SemanticAct`? The closed sum has to take a position; both
   options have plausibility but the wrong choice locks in.
3. **Per-arm failure handling.** If arm 2 of a 3-arm sequence
   throws, does the resolver invalidate the cached sequence (refusing
   to use it next page), or just the failed arm, or the whole crawl?
   Each policy has callers; v1 picking arbitrarily makes the v2
   reconsideration breaking.

Until a real caller proves the shape, v1 keeps `SemanticAct → single
arm`; multi-step intents need multi-step caller code. Same v2 deferral
discipline as ADR-0050's `(a)` (per-host cache keying) and `(e)`
(multi-candidate resolutions).

### (i) Auto-wait as an explicit `TimeoutMs` field on `Fill`

`Fill(string Selector, string Value, int TimeoutMs)` with the 30 s
default at the builder level. **Rejected.** Promoting an implicit
policy to an explicit field bloats every Fill call (`new
PageAction.Fill(sel, val, 30_000)` writes 30_000 for no reason); the
codec gains a third field whose value is almost always the default;
the tool descriptor's JSON Schema gains an optional field the LLM
will mostly omit and occasionally hallucinate.

The disciplined composition for a custom timeout is `WaitForSelector(sel,
custom_timeout) + Fill(sel, val)`; the outer arm's explicit timeout
shadows the inner safety net. Same pattern as Playwright's `page.fill`
vs `locator(sel).waitFor({timeout: …}).then(fill)`.

The *flagged-ambiguity* this introduces is real and gets recorded in
CONTEXT.md: **`Fill` and `ScrollIntoView` are the only arms carrying
an implicit timeout policy. Every other arm with a timeout
(`WaitForSelector`) makes it an explicit field.** The discipline:
**implicit when the timeout is the safety net for the common case;
explicit when it is the load-bearing parameter that varies per call.**
A future arm-author should consult this rule before adding a fourth
implicit-timeout arm.

### (j) `Hover(selector)` arm

Mouse-hover an element to trigger CSS `:hover` rules or JS hover
listeners. **Rejected, deferred.** Firecrawl doesn't have it; no
WebReaper user has asked for it; the closed sum stays seven-plus-three
arms in v1. If a caller surfaces, a future ADR adds it (one more
nested record per ADR-0035's extension pattern).

### (k) `SelectOption(dropdown, value)`, iframe switching, file upload, drag-and-drop

The remaining Playwright form-interaction primitives. **Rejected,
deferred each.** All four are niche relative to `Fill` + `Press` +
`ScrollIntoView`; bundling them into one ADR makes the scope
unbearable. Each surfaces as a follow-up ADR if a caller proves the
shape; the same one-arm-per-ADR pattern this ADR follows for
`Sequence` and `Hover`.

### (l) `Screenshot` / `PDF` / mid-sequence `Scrape` arms

Firecrawl's three observation actions. **Rejected; wrong category.**
These produce *output* (a bytes blob, a PDF, an extracted record);
the closed sum is mutation-only. The agent driver's
`decide → execute → re-observe` loop (ADR-0051) already handles
mid-sequence extraction at the agent level; multimodal observation
(`Screenshot`, `PDF`) is the subject of a separate future ADR
that widens `AgentState`, not `PageAction`. Listed here so future
review doesn't re-suggest folding them in.

### (m) MCP `scrape` / `extract` tool surface for the new arms

Adding action chains to the `WebReaper.Mcp` (ADR-0049) tool
parameters so MCP agents can drive form interaction. **Rejected for
this ADR's scope.** ADR-0049 explicitly scoped action chains out of
v1 (the MCP tools take a URL, not a workflow); adding them is its
own ADR with its own MCP-tool-arg-shape decisions (JSON-encoded
action arrays vs separate tool methods vs builder mini-DSL). Listed
to preempt the "Fill is added so MCP gets it too" follow-up; that's
a separate decision on a separate surface.

## Bounded scope

### v1 does

- Adds `PageAction.Fill(string Selector, string Value)`,
  `PageAction.Press(string Key)`, `PageAction.ScrollIntoView(string Selector)`
  to the closed sum.
- Adds `.Fill(selector, value)` / `.Press(key)` /
  `.ScrollIntoView(selector)` fluent methods to
  `PageActionBuilder`.
- Adds wire-format tags `"fill"` / `"press"` / `"scrollIntoView"` to
  the codec.
- Dispatches all three in both `WebReaper.Cdp` and `WebReaper.Playwright`
  transports. `Fill` and `ScrollIntoView` reuse the
  `CdpPageActionDispatcher.WaitForSelectorAsync` helper for the
  CDP-side auto-wait; Playwright's native auto-wait covers Playwright.
- Adds tool descriptors `ActFill` / `ActPress` / `ActScrollIntoView`
  to both the brain and resolver registries (ADR-0060). Brain: 10 →
  13 tools. Resolver: 6 → 9 tools.
- Extends the `LlmActionResolver` prompt's whitelist from four shapes
  to seven.
- Documents the implicit-30s-timeout discipline in CONTEXT.md's
  flagged-ambiguities.

### v1 does not

- No `Hover`, `Sequence`, `SelectOption`, iframe switch, file upload,
  drag-and-drop, `Screenshot`, `PDF`, mid-sequence `Scrape`. Each is
  a separate future ADR if a caller surfaces.
- No deprecation of `ScrollToEnd`. The existing arm stays as-is; no
  `[Obsolete]`, no `Scroll(Down, null)` alias.
- No `ScrollDirection` enum.
- No `FillOptions` / `PressOptions` / `ScrollOptions` configuration
  records.
- No new MCP `scrape` / `extract` tool parameter exposing action
  chains. The MCP satellite stays single-shot per ADR-0049.
- No agent-driver-specific changes. `AgentDecision.Act(PageAction)`
  (ADR-0051) accepts the new arms by base-type substitutability; no
  agent-side surface widening.

## Implementation outline

Eight file edits, sized by the arm-local pattern (PR #134, re-apply
in flight on a separate branch; this ADR's cost numbers assume that
pattern is on master by implementation time).

1. **`WebReaper/Domain/PageActions/PageAction.cs`**: three new nested
   `sealed record` arms. Class-doc updates "seven arms" → "ten arms"
   and notes the implicit 30 s auto-wait on `Fill` / `ScrollIntoView`.
2. **`WebReaper/Builders/PageActionBuilder.cs`**: three new fluent
   methods with `ArgumentException.ThrowIfNullOrWhiteSpace` validation,
   sibling to `Click` / `WaitForSelector`. Each appends one new arm
   record to the internal list.
3. **`WebReaper/Serialization/Converters/PageActionJsonConverter.cs`**:
   three new write-arm switch cases, three new read-arm switch
   cases. The `Require(...)` helper continues to enforce non-null
   selectors / values / keys with actionable JSON-exception messages.
4. **`WebReaper.Cdp/CdpPageActionDispatcher.cs`**: three new switch
   arms:
   - `Fill`: call `WaitForSelectorAsync` (ADR-0057 helper), then issue
     one `Runtime.evaluate` payload that runs the React-friendly
     native-setter trick + `dispatchEvent(input/change)` JS. The
     payload throws if the element fails the shape check or is
     disabled; the dispatcher surfaces that as a typed exception.
   - `Press`: call `Input.dispatchKeyEvent` twice (`keyDown`,
     `keyUp`) with the resolved CDP fields. A static
     `KeyStringToCdpKey` map handles the conversion; an unknown key
     throws `ArgumentException`.
   - `ScrollIntoView`: call `WaitForSelectorAsync` then `Runtime.evaluate`
     `document.querySelector(sel).scrollIntoView()`.
5. **`WebReaper.Playwright/PlaywrightPageLoadTransport.cs`**: three
   new switch arms:
   - `Fill`: `await page.FillAsync(a.Selector, a.Value)` (one line:
     native auto-wait + framework events).
   - `Press`: `await page.Keyboard.PressAsync(a.Key)`.
   - `ScrollIntoView`: `await page.Locator(a.Selector).ScrollIntoViewIfNeededAsync()`.
6. **`WebReaper.AI/Tools/PageActionTools.cs`** (post-PR-#134 file
   layout): three new nested static classes (`Fill`, `Press`,
   `ScrollIntoView`), each with `Name` const + `Descriptor` JSON
   Schema + `FromArguments` builder. The `ForBrain()` and
   `ForResolver()` lists each append three entries.
7. **`WebReaper.AI/LlmActionResolver.cs`**: the prompt's whitelist
   updates from four shapes to seven (or the prompt is structured
   to enumerate from the registry; implementation detail). The
   `ParseActionTool` switch gains three one-line entries per the
   PR #134 uniform pattern.
8. **`WebReaper.AI/LlmAgentBrain.cs`**: same one-line-per-new-arm
   addition to `ParseDecisionTool`.

### Tests (the slice's verification surface)

- `WebReaper.Tests/WebReaper.UnitTests/PageActionBuilderTests`:
  three new test methods (one per fluent method); each asserts the
  builder appends the right arm with the right typed fields.
- `WebReaper.Tests/WebReaper.UnitTests/StjSerializationTests`: three
  new round-trip tests (`Fill` / `Press` / `ScrollIntoView` through
  the codec).
- `WebReaper.Tests/WebReaper.UnitTests/PayloadShellTests`: verify a
  `ScraperConfig` carrying a `Fill` arm round-trips through the
  config payload shell.
- `WebReaper.Tests/WebReaper.UnitTests/SemanticActDispatchTests`:
  extend the resolver-stub fake to return a `Fill` or `Press` arm;
  pin "the cache stores the resolved arm regardless of which of the
  nine concrete arm shapes it is."
- `WebReaper.Tests/WebReaper.Cdp.Tests/CdpPageActionDispatchTests`:
  three new dispatch tests:
  - `Fill` against a `FakeCdpSession` asserts the
    `WaitForSelectorAsync` poll happened, the native-setter JS
    payload was evaluated, and `input` + `change` events were
    dispatched.
  - `Press("Enter")` asserts the `Input.dispatchKeyEvent` call
    carried the Enter key code (13 / `\r`).
  - `Press("Control+A")` asserts the modifier bitmask (2) and the
    key code for `A`.
  - `Press("InvalidKey")` asserts the `ArgumentException` with the
    offending key in the message.
  - `ScrollIntoView` asserts the poll + `scrollIntoView()` JS.
- `WebReaper.Tests/WebReaper.AI.Tests/LlmActionResolverTests`: three
  new resolution tests (LLM returns each new shape → resolver
  constructs the right arm). One additional test pins that the
  prompt's whitelist contains the new shape names.
- `WebReaper.Tests/WebReaper.AI.Tests/AgentDecisionToolsTests` (or
  the post-PR-#134 `PageActionToolsTests` equivalent): three new
  schema-pinning tests + a brain-registry-count test (10 → 13) + a
  resolver-registry-count test (6 → 9).
- `WebReaper.AotSmokeTest`: extend the existing PageAction
  round-trip smoke to include `Fill` / `Press` / `ScrollIntoView`
  in the closed-sum exercise. Reflection-free codec stays AOT-clean.

### CONTEXT.md

One new bullet under `## Flagged ambiguities`:

> **`Fill` and `ScrollIntoView` are the only `PageAction` arms with
> an implicit timeout policy.** Every other arm carrying a timeout
> (`WaitForSelector`) takes it as an explicit `int TimeoutMs` field.
> The discipline that draws the line: a timeout that is the safety
> net for the common case stays implicit (30 s default, documented
> on the arm record); a timeout that is the load-bearing parameter
> varying per call stays explicit. ADR-0074 records the rule so a
> future arm-author can apply it; the v1 implicit-timeout set is
> closed at these two arms.

No new glossary entry; the new arms inherit the existing
`PageAction` semantics (no new domain term), and the
single-glossary-entry-per-arm pattern (the AI-native section)
applies only to arms that introduce *new* concepts. `Fill` /
`Press` / `ScrollIntoView` are ordinary closed-sum arms.

### CHANGELOG.md

One new section under `## 10.1.0 (in progress)` (or folded into `##
10.0.2 (in progress)` if v10.0.2 hasn't tagged when the slice lands;
adding public types is SemVer-minor but the additive-types-as-patch
interpretation is common in OSS and v10.0.2's "post-launch refactors"
header accommodates it). Entry follows the ADR-0050 / ADR-0057 entry
shape: ADR link, the gap closed, the arm shapes, the implementation
file list, the test count change.

### CLAUDE.md

One new bullet under "Gotchas":

> ADR-0074: `PageAction.Fill` and `PageAction.ScrollIntoView` carry
> an implicit 30 s auto-wait policy on selector resolution (CDP
> reuses ADR-0057's `WaitForSelectorAsync` helper; Playwright
> auto-waits natively). The arm records intentionally do not carry
> a `TimeoutMs` field; composition with `WaitForSelector(sel,
> custom_timeout)` covers the rare non-30s case. The implicit-timeout
> discipline is recorded in CONTEXT.md's flagged-ambiguities and
> closed at these two arms; do not propagate to a fourth.

## SemVer

**Minor.** Three new public sealed-record arms, three new public
`PageActionBuilder` methods, three new tool descriptors. No existing
public surface changes shape; no arm is deprecated; no codec migration
is required for existing configs. Pre-v10.1 readers throw
`JsonException("unknown PageAction type")` on a forward-compat config
carrying a new arm; acceptable per SemVer's backward-compat-only
mandate.

The closed sum's `private` parent constructor means external types
can't extend `PageAction` directly; the three new arms are
internally-additive structural widening, externally a pure
type-set growth.

CHANGELOG home is `## 10.1.0 (in progress)` if v10.0.2 has tagged by
slice merge; otherwise the entry folds into `## 10.0.2 (in progress)`
under the same additive-additions discipline that section already
carries (`WebReaper.Mcp` browser wiring + `LlmToolArguments` helper
are both additive-public-types).

## References

- [ADR-0001](0001-crawl-outcome-closed-sum.md): the closed-sum
  pattern (`CrawlOutcome`) the seven existing `PageAction` arms +
  the three new ones share.
- [ADR-0035](0035-pageaction-closed-sum.md): the `PageAction`
  closed-sum origin; the structural discipline this ADR widens by
  three arms. The "construct only via nested arms; not extensible"
  invariant carries; the wire-format additive pattern follows the
  same codec shape.
- [ADR-0050](0050-semantic-page-actions.md): `SemanticAct` +
  `IActionResolver`. The new arms become resolution targets in the
  satellite resolver's whitelist; the multi-step-intent v2 deferral
  is recorded as [Considered options (h)](#h-sequencearm1-arm2--composite-arm-in-v1).
- [ADR-0057](0057-cdp-network-idle.md): CDP capability extension
  precedent. `Fill` and `ScrollIntoView` reuse the
  `CdpPageActionDispatcher.WaitForSelectorAsync` helper this ADR's
  slice contributed; the implicit-30s pattern is the same shape as
  ADR-0057's 30 s total-timeout safety net.
- [ADR-0060](0060-tool-calling-brain-and-action-resolver.md): tool
  registries for the brain (10 → 13 tools) and resolver (6 → 9
  tools). Fork 8's `ActSemanticAct`-absence-on-the-resolver
  invariant is preserved; no new-arm tool reintroduces a SemanticAct
  loop.
- [ADR-0049](0049-mcp-server-satellite.md): `WebReaper.Mcp`
  satellite; the surface explicitly NOT extended by this ADR (see
  [Considered options (m)](#m-mcp-scrape--extract-tool-surface-for-the-new-arms)).
- PR #134 (`refactor(ai): arm-local tool projection`): the arm-local
  `PageActionTools.<Arm>` pattern this ADR's tool-registry additions
  follow. Re-applied via PR #139 (the original was stranded by the
  [[stacked-pr-merge-gotcha]]); the post-#134 file layout is on
  master, and the cost numbers in [§ Implementation outline](#implementation-outline)
  assume that layout.
- Firecrawl `/scrape` API reference
  (`https://docs.firecrawl.dev/api-reference/endpoint/scrape`): the
  9-action-types vocabulary this ADR achieves parity against (closing
  3 of 5 real gaps; the other 2 of 5, `screenshot` and `pdf`, are
  deferred as observation actions per [§ Considered options (l)](#l-screenshot--pdf--mid-sequence-scrape-arms)).
