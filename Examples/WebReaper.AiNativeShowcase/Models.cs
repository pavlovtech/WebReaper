using WebReaper.Extraction.Attributes;

namespace WebReaper.AiNativeShowcase;

// ADR-0045 — [ScrapeSchema] marks this partial class for the Roslyn
// source generator. At compile time it emits:
//   * `public static readonly Schema Schema = new() { ... }` — the
//     schema the fold consumes, derived from each [ScrapeField].
//   * `public static BlogPost Materialize(JsonObject json)` — a
//     reflection-free constructor that round-trips a JSON object back
//     to a strongly-typed BlogPost.
// AOT-clean — no reflection, no dynamic; the generator runs at compile
// time and emits ordinary C# the AOT publish trims and inlines.
[ScrapeSchema]
public partial class BlogPost
{
    [ScrapeField(".text-3xl.font-bold")]
    public string? Title { get; set; }

    [ScrapeField(".max-w-max.prose.prose-dark")]
    public string? Body { get; set; }
}
