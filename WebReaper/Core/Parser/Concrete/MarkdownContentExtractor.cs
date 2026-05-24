using System.Text.Json.Nodes;
using WebReaper.Core.Markdown;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Concrete;

/// <summary>
/// The Markdown adapter of the <see cref="IContentExtractor"/> seam
/// (ADR-0040): turn a loaded page into LLM-ready Markdown without a
/// <see cref="Schema"/>. Reached via the second
/// <see cref="WebReaper.Builders.ICrawlSeed"/> terminal
/// <c>AsMarkdown()</c>, this is the funnel's no-schema wedge — the
/// smallest possible call returns LLM-ready text.
/// <para>
/// As of ADR-0063 the adapter is a thin shell over the public
/// <see cref="HtmlToMarkdown"/> primitive in
/// <c>WebReaper.Core.Markdown</c> — the heuristic, strip list, and GFM
/// rendering all live in the primitive. The shell delegates to
/// <see cref="HtmlToMarkdown.ExtractMainContent"/> and projects the
/// result as the <see cref="JsonObject"/> shape every <see cref="WebReaper.Sinks.Abstract.IScraperSink"/>
/// consumes: <c>title</c> (the first <c>&lt;h1&gt;</c> in the main
/// content, else <c>&lt;title&gt;</c>) and <c>markdown</c> (GFM-flavoured
/// rendering of the main content area).
/// </para>
/// <para>
/// Callers needing just the Markdown string (the LLM extractor's
/// pre-clean, the change-tracking processor's hash, the agent engine's
/// state-building) should reach <see cref="HtmlToMarkdown.Convert"/>
/// directly — going through this adapter constructs and discards the
/// <see cref="JsonObject"/> wrapping for no reason.
/// </para>
/// <para>
/// The <see cref="Schema"/> parameter is accepted and ignored — the
/// Markdown strategy has no place to project a Schema onto. The
/// strategy-local schema requirement is documented on the seam
/// (ADR-0040 §3). ADR-0031 folds the page URL in under <c>"url"</c> at
/// <c>ParsedData</c> construction — this extractor never touches that
/// key.
/// </para>
/// </summary>
public sealed class MarkdownContentExtractor : IContentExtractor
{
    /// <inheritdoc/>
    public Task<JsonObject> ExtractAsync(string document, Schema? schema)
    {
        // ADR-0040: the Schema is strategy-locally ignored — the Markdown
        // extractor has no use for it. The seam's nullability already
        // advertised this variation (ADR-0039); the doc widened.
        _ = schema;

        // ADR-0063: delegate to the public primitive. The shell is one
        // call (extract main content) + one wrap (project as JsonObject).
        var content = HtmlToMarkdown.ExtractMainContent(document);

        var result = new JsonObject
        {
            ["title"] = JsonValue.Create(content.Title),
            ["markdown"] = JsonValue.Create(content.Markdown)
        };

        return Task.FromResult(result);
    }
}
