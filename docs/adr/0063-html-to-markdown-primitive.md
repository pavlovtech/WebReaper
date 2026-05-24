# `HtmlToMarkdown` — promote the no-schema conversion to a public primitive

## Status

**Accepted — implemented** (2026-05-24). Fifth ADR of the post-AI-
native-wave deepening campaign. Extracts the HTML→Markdown conversion
(previously buried in `MarkdownContentExtractor`) into a public static
primitive under `WebReaper.Core.Markdown`. `MarkdownContentExtractor`
became a thin `IContentExtractor` shell. Resolves the structural
awkwardness where three callers reached for the *function* but had to
instantiate and call the *adapter*. Folded into the same v10.x release.

## Context

`MarkdownContentExtractor` (ADR-0040) is the second `IContentExtractor`
adapter — the no-schema Markdown strategy reached via the `AsMarkdown()`
seed terminal. Internally it is two things welded together:

1. **A pure HTML-to-Markdown conversion function.** Open the document
   via AngleSharp; pick a main content root (`<article>`/`<main>`/
   `[role=main]`/`<body>`); strip non-content descendants
   (nav/aside/footer/script/...); render the surviving DOM as GFM
   Markdown; resolve the title (`<h1>` inside main, else `<title>`);
   collapse whitespace; trim. Stateless, deterministic, AOT-clean.
2. **An `IContentExtractor` shell** that takes a `(document, schema)`
   pair, runs the conversion, and projects the result as a
   `JsonObject { "title", "markdown" }` for the sinks.

Three sites today reach for the *function*, not the adapter:

| Caller | Current code | Awkwardness |
|---|---|---|
| `LlmContentExtractor.PreCleanToMarkdownAsync` | `await _markdown.ExtractAsync(document, null); md["markdown"]?.GetValue<string>()` | Calls `ExtractAsync` to discard the JsonObject wrapping and pull the raw string back out. |
| `LlmSelectorRepairer.PreCleanToMarkdownAsync` | same pattern | same |
| `ChangeTrackingProcessor.ProcessAsync` | `await _markdown.ExtractAsync(context.Html, schema: null); md["markdown"]?.GetValue<string>(); ComputeHash(string)` | Hashes the Markdown for change-tracking; constructs / discards the `JsonObject` for no reason. |
| `AgentEngine.RunAsync` (state-building) | `await MarkdownView.ExtractAsync(pageHtml, schema: null); markdownRecord["markdown"]?.GetValue<string>()` | Same — the engine wants a string. |

The awkwardness is structural:

- The seam `IContentExtractor` returns `Task<JsonObject>` — *the right
  shape for the sink*. None of these four callers is a sink. They
  build a string, wrap it in a `JsonObject`, then immediately unwrap.
- The adapter is *stateless* (no constructor params; `new
  MarkdownContentExtractor()` creates a value-typed instance) — but
  it's modelled as a per-caller field because that was the ADR-0039
  shape. Three of the four callers hold one as a private field for
  the lifetime of the satellite class.
- The Schema-strategy-locality on the seam (`Schema?` accepted-and-
  ignored on `MarkdownContentExtractor`) is *strategy-local* by
  ADR-0040 design; the four callers passing `schema: null` are using
  the parameter for nothing.

Promoting the conversion to a public primitive resolves all three
awkwardnesses. The adapter survives — it's still the `IContentExtractor`
the `AsMarkdown()` seed terminal hooks. It just becomes the thin
shell over the primitive.

### What the primitive exposes

Two overloads, mirroring the two ways the function is consumed:

- `Convert(string html) → string` — just the Markdown body. The three
  string-only callers (LLM pre-clean, repairer pre-clean, change-
  tracking hash, agent state) all use this.
- `ExtractMainContent(string html) → MainContent` — `{ Title,
  Markdown }`. The adapter and any future caller wanting both pieces
  uses this.

### Where it lives

A new namespace `WebReaper.Core.Markdown` for the primitive. Not under
`WebReaper.Core.Parser` (which is the *seam* layer for
`IContentExtractor` and the deterministic `SchemaFold` adapter — the
primitive is not an extraction *seam*; it's a function). Not at the
project root (the project root is the public-API namespace; the
primitive is a domain function, deserves a sub-namespace).

`WebReaper.Core.Markdown` keeps the door open for related primitives
in v2 (a future `HtmlSanitizer` removing scripts before LLM input; a
`Markdown.TableExtractor` if the repositioning-plan §2.5's CLI ever
asks for tables-as-CSV). The namespace stays narrow today: one
primitive + one record.

## Decision

Three pieces — one new public primitive, one new public record, one
shrunken adapter. No seam changes; no builder changes. The
`AsMarkdown()` seed terminal still wires `MarkdownContentExtractor`;
the shell just becomes thin.

### 1. `HtmlToMarkdown` — the public primitive

`WebReaper/Core/Markdown/HtmlToMarkdown.cs`. Public static class:

```csharp
namespace WebReaper.Core.Markdown;

/// <summary>
/// Pure HTML→Markdown conversion. The no-schema primitive used by
/// <see cref="WebReaper.Core.Parser.Concrete.MarkdownContentExtractor"/>,
/// the LLM extractor's pre-clean step (ADR-0044), the LLM repairer's
/// pre-clean (ADR-0047), the change-tracking processor's hash
/// (ADR-0048), and the agent engine's state-building (ADR-0051).
/// AOT-clean; no LLM dependency. The tag-based Readability heuristic
/// (article → main → [role=main] → body) and the GFM rendering are
/// the ADR-0040 grammar — unchanged.
/// </summary>
public static class HtmlToMarkdown
{
    /// <summary>
    /// Convert HTML to its main-content Markdown rendering. Equivalent to
    /// <c>ExtractMainContent(html).Markdown</c>; the title is computed
    /// and discarded. Use this overload when only the body is needed
    /// (LLM pre-clean, change-tracking hash, agent state).
    /// </summary>
    public static string Convert(string html)
        => ExtractMainContent(html).Markdown;

    /// <summary>
    /// Extract the main-content title and Markdown rendering from HTML.
    /// Title is the first <c>&lt;h1&gt;</c> inside the chosen main-content
    /// root after stripping non-content descendants; falls back to
    /// <c>&lt;head&gt;&lt;title&gt;</c> if no <c>&lt;h1&gt;</c> is present.
    /// Markdown is the GFM rendering of the surviving DOM. Use this
    /// overload when both fields are needed (the
    /// <see cref="WebReaper.Core.Parser.Concrete.MarkdownContentExtractor"/>
    /// adapter's case).
    /// </summary>
    public static MainContent ExtractMainContent(string html)
    {
        // ... the heuristic-pick + strip + walk implementation,
        //     moved verbatim from MarkdownContentExtractor.
    }
}
```

The function is synchronous (`string → string` / `string →
MainContent`). The current `MarkdownContentExtractor.ExtractAsync`
is async-shaped only because AngleSharp's `BrowsingContext.OpenAsync`
returns a Task — there's no actual async work in the conversion. The
primitive uses `OpenAsync(...).GetAwaiter().GetResult()` at the
boundary, or — preferred — the synchronous AngleSharp
`HtmlParser.ParseDocument(string)` path that skips the
`BrowsingContext` configuration loader (and is faster for the
no-loader case anyway). The implementation slice picks the sync
parser.

### 2. `MainContent` — the public record

`WebReaper/Core/Markdown/MainContent.cs`. Public record:

```csharp
namespace WebReaper.Core.Markdown;

/// <summary>
/// The two-field result of <see cref="HtmlToMarkdown.ExtractMainContent"/>:
/// the resolved title (post-strip) and the GFM-rendered Markdown body.
/// Pure-data; no behaviour.
/// </summary>
public sealed record MainContent(string Title, string Markdown);
```

### 3. `MarkdownContentExtractor` shrinks to a thin shell

```csharp
public sealed class MarkdownContentExtractor : IContentExtractor
{
    /// <inheritdoc/>
    public Task<JsonObject> ExtractAsync(string document, Schema? schema)
    {
        // ADR-0040: Schema is strategy-locally ignored.
        _ = schema;
        var content = HtmlToMarkdown.ExtractMainContent(document);
        return Task.FromResult(new JsonObject
        {
            ["title"] = JsonValue.Create(content.Title),
            ["markdown"] = JsonValue.Create(content.Markdown)
        });
    }
}
```

~20 lines (down from ~600). The class is preserved — `AsMarkdown()`
needs the `IContentExtractor` adapter — but its body is now exactly
what an adapter-of-a-primitive should be: one call, one wrap.

### 4. Four callers swap to the primitive

- **`LlmContentExtractor.PreCleanToMarkdownAsync`** removed; the
  pre-clean inlined:
  ```csharp
  var content = HtmlToMarkdown.ExtractMainContent(document);
  return string.IsNullOrEmpty(content.Title)
      ? content.Markdown
      : $"# {content.Title}\n\n{content.Markdown}";
  ```
  The `MarkdownContentExtractor` field is removed.
- **`LlmSelectorRepairer.PreCleanToMarkdownAsync`** removed; the
  pre-clean inlined to `HtmlToMarkdown.Convert(document)`. The field
  is removed.
- **`ChangeTrackingProcessor.ProcessAsync`** inlines:
  ```csharp
  var markdown = HtmlToMarkdown.Convert(context.Html);
  var hash = ComputeHash(markdown);
  ```
  The `MarkdownContentExtractor` field is removed.
- **`AgentEngine.RunAsync`** inlines:
  ```csharp
  var pageMarkdown = HtmlToMarkdown.Convert(pageHtml);
  if (pageMarkdown.Length > _maxPageMarkdownChars)
      pageMarkdown = pageMarkdown[.._maxPageMarkdownChars];
  ```
  The static `MarkdownView` field is removed. The try / catch around
  the Markdown rendering stays (a corrupt page might break the
  parser); the catch falls through to a truncated raw-HTML view.

### Bounded scope (v1)

- **No heuristic-tweak options.** v1 keeps the one canonical
  heuristic (article → main → [role=main] → body). A future caller
  needing control (a tighter selector, a different strip list,
  Mozilla-Readability-style scoring) swaps in a custom
  `IContentExtractor`; the primitive's contract is "the canonical
  one." A `HtmlToMarkdownOptions` knob is a v2 deferral.
- **No image / link policy.** The current renderer emits
  `![alt](src)` and `[text](href)` verbatim; same in v1. Future
  knobs (`SkipImages`, `ResolveRelativeUrls`) are v2.
- **No streaming.** The primitive returns the whole string. A
  streaming overload is a v2 deferral.
- **`MarkdownContentExtractor` stays public.** ADR-0040's
  `AsMarkdown()` build terminal needs the `IContentExtractor`
  adapter; deleting the class would break the seed terminal.

## Considered options

### Fork 1 — Module shape

| Option | What | Verdict |
|---|---|---|
| (a) Static class | `public static class HtmlToMarkdown { Convert(...); ExtractMainContent(...); }`. | **Recommended.** Pure function, no state, no constructor params. Static class is the structurally honest shape. |
| (b) Instance class | `public sealed class HtmlToMarkdownConverter { ... }`. Consumers `new` an instance. | Rejected. Instance state isn't there to carry; the class would exist just to be `new`'d at every call site. |
| (c) Interface `IHtmlToMarkdown` | Define the seam for the conversion primitive. | Rejected. ADR-0036 — "shape from the second adapter, not for it." There is no second adapter; the function is the canonical one. |

### Fork 2 — Public vs. internal

| Option | What | Verdict |
|---|---|---|
| (a) Public | Consumer-authored AI adapters reuse the canonical primitive. | **Recommended.** Same reasoning as ADR-0059's `LlmCall<T>` public posture; composability of the satellite. A consumer's custom processor wanting a clean Markdown view of the page reaches the same primitive the built-in ones do — consistent rendering, consistent strip list, no drift. |
| (b) Internal to `WebReaper` core | `MarkdownContentExtractor` re-exposes via its public method. | Rejected. Forces consumers back through the adapter shell, re-introducing the `JsonObject`-wrap-and-unwrap dance for what is a pure function. |

### Fork 3 — Namespace

| Option | What | Verdict |
|---|---|---|
| (a) `WebReaper.Core.Markdown` | New namespace for Markdown-related primitives. | **Recommended.** Primitive, not extraction-seam-specific. Room for related primitives in v2 (sanitizer, table extractor). Mirrors the `WebReaper.Core.Crawling` / `WebReaper.Core.Actions` / `WebReaper.Core.Parser` per-domain pattern. |
| (b) `WebReaper.Markdown` (project root) | Public API namespace, no nesting. | Rejected. The project-root namespace is for end-user types (`ScraperEngineBuilder`, `Agent`, etc.); the primitive is a domain function — sub-namespace is the discipline. |
| (c) `WebReaper.Core.Parser.Markdown` | Under `Parser`. | Rejected. Parser is for extraction *seams*; the primitive isn't a seam. |

### Fork 4 — Return shape: string vs. record vs. both

| Option | What | Verdict |
|---|---|---|
| (a) Two overloads: `Convert(html) → string` + `ExtractMainContent(html) → MainContent` | Caller picks. | **Recommended.** The string overload is the high-frequency caller (three of four current sites); the record overload is the adapter's case. Both names read at their call sites. |
| (b) String only | One method; record callers can split on the H1 themselves. | Rejected. Re-parsing for the title is wasteful and brittle; the heuristic for title selection is part of the primitive's contract. |
| (c) Record only | Callers wanting just-string do `.Markdown`. | Rejected. The four high-frequency callers want a string; the record-then-property dance at every site is the same awkwardness the ADR exists to remove. |

### Fork 5 — Configurability

| Option | What | Verdict |
|---|---|---|
| (a) No options in v1 | One canonical heuristic. | **Recommended.** Same posture as `LinkExtractor` (ADR-0036) — one canonical implementation, AOT-clean, no per-caller knob. Custom heuristics live in custom `IContentExtractor`s. |
| (b) `HtmlToMarkdownOptions` knob bag | Per-call options (strip list, root selector preference, etc.). | Rejected (v2 deferral). Speculative; no caller has named the knobs. |
| (c) Per-call delegate hooks | `Convert(html, customStrip: el => bool)`. | Rejected (v2 deferral). Same — speculative. |

### Fork 6 — Should `MarkdownContentExtractor` be deleted entirely

| Option | What | Verdict |
|---|---|---|
| (a) Keep as the `IContentExtractor` adapter | ~20-line shell over the primitive. | **Recommended.** ADR-0040's `AsMarkdown()` build terminal wires the adapter into `ScraperConfig`; the adapter participates in the registration seam (`WithContentExtractor`). Deleting the class breaks the terminal. |
| (b) Delete; `AsMarkdown()` constructs an inline adapter | The seed terminal builds an anonymous `IContentExtractor` lambda. | Rejected. Two indirections (lambda + delegate-adapter) where one (the existing class) suffices. The class also has the `[ScrapeSchema]` doc-anchor — it's a real type with documentation, not an anonymous wrapper. |

## Consequences

- **The "Markdown extractor wraps Markdown extractor" awkwardness
  resolves.** `LlmContentExtractor` no longer holds a private
  `MarkdownContentExtractor` field to call for pre-cleaning; it
  calls the primitive directly. Same for `LlmSelectorRepairer`,
  `ChangeTrackingProcessor`, `AgentEngine`.
- **One canonical conversion function exists.** Currently the
  conversion is reached via `_markdown.ExtractAsync(...,
  schema: null)["markdown"]?.GetValue<string>()` — eight tokens that
  could be `HtmlToMarkdown.Convert(html)`. Maintenance burden drops:
  one place to read, one place to change.
- **`MarkdownContentExtractor` becomes ~20 lines.** The 600 lines of
  rendering / stripping / heuristic move into `HtmlToMarkdown`; the
  adapter shell delegates.
- **No public API breakage.** `MarkdownContentExtractor`'s public
  surface (the constructor + `ExtractAsync`) stays the same; callers
  who use the adapter continue to work. The new primitive is
  additive.
- **AOT-clean.** Synchronous, no reflection. The primitive is one
  more piece of AOT-safe core surface (the AI satellites are non-AOT,
  but the primitive is in core where AOT-cleanliness is a contract).
- **`ChangeTrackingProcessor`'s `JsonObject`-construct-and-discard
  for hashing disappears.** The processor hashes the string directly;
  no allocation for the discarded wrapper.
- **CONTEXT.md** updates **Markdown extraction** entry to note it is
  a thin shell over the new primitive; adds **HTML-to-Markdown
  primitive** as a sibling term (the function used by Markdown
  extraction, LLM extractor, agent engine, change tracker).
- **CLAUDE.md** gets a gotcha — `HtmlToMarkdown` is the public
  primitive in `WebReaper.Core.Markdown`; `MarkdownContentExtractor`
  is the thin `IContentExtractor` shell; callers needing just-
  Markdown should use the primitive directly.

## Bounded scope (v1)

- **`HtmlToMarkdownOptions`** — one canonical heuristic in v1.
- **`HtmlSanitizer`** sibling primitive — out of scope.
- **`Markdown.TableExtractor`** — out of scope (the GFM table
  rendering inside `HtmlToMarkdown` is sufficient for the
  `LlmContentExtractor`'s pre-clean today).
- **Streaming** — single-shot in v1.
- **`AsyncConvert`** — sync only in v1; AngleSharp's sync parser is
  fast enough.

## Implementation (slice, when accepted)

**Core primitive:**

1. **`WebReaper/Core/Markdown/HtmlToMarkdown.cs`** — new public static
   class. The body is moved verbatim from
   `MarkdownContentExtractor.ExtractAsync`'s heuristic-pick + strip +
   walk; the AngleSharp `BrowsingContext.OpenAsync` is replaced with
   `HtmlParser.ParseDocument` for the sync path.
2. **`WebReaper/Core/Markdown/MainContent.cs`** — new public record.

**Core adapter shrinks:**

3. **`WebReaper/Core/Parser/Concrete/MarkdownContentExtractor.cs`** —
   the 600-line body collapses to the ~20-line shell described
   above. Doc-comment preserved (ADR-0040 reference + the strategy-
   local schema note).

**Caller updates:**

4. **`WebReaper.AI/LlmContentExtractor.cs`** — `_markdown` field
   removed; `PreCleanToMarkdownAsync` becomes `PreCleanToMarkdown`
   (synchronous) calling the primitive.
5. **`WebReaper.AI/LlmSelectorRepairer.cs`** — same change.
6. **`WebReaper/Processing/Concrete/ChangeTrackingProcessor.cs`** —
   `_markdown` field removed; inline `HtmlToMarkdown.Convert(...)`.
7. **`WebReaper/Core/Agent/Concrete/AgentEngine.cs`** — static
   `MarkdownView` field removed; inline `HtmlToMarkdown.Convert(...)`.
   The try/catch around the Markdown rendering stays.

**Tests:**

8. **`WebReaper.Tests/WebReaper.UnitTests/HtmlToMarkdownTests.cs`** —
   new file; pin the canonical heuristic (article wins; main wins
   when no article; etc.); pin the strip list; pin title resolution
   (h1-inside-main; fallback to head/title; never the stripped
   nav-h1); pin GFM rendering (headings, paragraphs, lists, blockquote,
   code-fence with language, table, links, images, inline elements).
   These tests are an extracted-and-renamed superset of the existing
   `MarkdownContentExtractorTests`; the parsing/rendering logic is
   unchanged so the assertions transplant.
9. **`WebReaper.Tests/WebReaper.UnitTests/MarkdownContentExtractorTests.cs`**
   (existing) — slim down to "shell delegates to the primitive": one
   round-trip test that confirms the `JsonObject` shape; the
   rendering tests move to `HtmlToMarkdownTests`.
10. **`WebReaper.Tests/WebReaper.AI.Tests/LlmContentExtractorTests.cs`**
    — existing tests should pass unchanged (the pre-clean's output is
    the same string).
11. **`WebReaper.Tests/WebReaper.UnitTests/ChangeTrackingProcessorTests.cs`**
    — existing tests pass unchanged (the hash input is the same
    string).
12. **`WebReaper.Tests/WebReaper.UnitTests/AgentEngineDriverTests.cs`**
    — existing tests pass unchanged.

**Docs:**

13. **CONTEXT.md** — update **Markdown extraction** entry; add **HTML-
    to-Markdown primitive** term; relationship line.
14. **CLAUDE.md** — gotcha on the primitive being the public callable;
    the adapter being the thin shell.

### Guardrails (when implemented)

- `dotnet build WebReaper.sln` — 0 errors.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — all pass;
  rendering tests moved to `HtmlToMarkdownTests` pass; the slimmed
  `MarkdownContentExtractorTests` passes.
- `dotnet test WebReaper.Tests/WebReaper.AI.Tests` — all pass
  unchanged.
- `WebReaper.AotSmokeTest` — unchanged (primitive is sync, AngleSharp-
  sync, no reflection; AOT-safe).

## References

- ADR-0036 — link extraction not a seam; the structural precedent —
  one canonical function in core, not a seam; the **content** half's
  primitive twin.
- ADR-0039 — `IContentExtractor` seam; the seam
  `MarkdownContentExtractor` adapts.
- ADR-0040 — Markdown extraction seed terminal; the build terminal
  the adapter wires into; the contract this ADR preserves.
- ADR-0044 — LLM extractor; the first caller of the primitive
  (currently goes through the adapter).
- ADR-0047 — self-healing selectors; the second caller.
- ADR-0048 — change-tracking processor; the third caller.
- ADR-0051 — agent driver; the fourth caller.
- ADR-0059 — `LlmCall<TResponse>`; co-located public primitive in
  the same wave — same composability discipline.
- ADR-0064 — `.UseAi(...)`; opts callers in transitively, since the
  per-role `WithLlm*` registrations all use the primitive via the
  satellite adapters.
