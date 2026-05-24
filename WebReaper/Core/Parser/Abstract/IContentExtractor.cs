using System.Text.Json.Nodes;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Abstract;

/// <summary>
/// The content-extraction seam: turn one loaded target page into the
/// structured record the sinks emit — the content half of crawling a page
/// (link discovery is the separate other half, the concrete
/// <c>LinkExtractor</c> function, ADR-0036).
/// <para>
/// Two adapters ship in core: <see cref="Concrete.SchemaFold{TNode}"/>, the
/// deterministic <see cref="Schema"/> fold over an
/// <see cref="ISchemaBackend{TNode}"/> (ADR-0002), and
/// <see cref="Concrete.MarkdownContentExtractor"/>, the no-schema Markdown
/// strategy reached via <c>ICrawlSeed.AsMarkdown()</c> (ADR-0040). The seam
/// is named for the task, not either adapter — extraction <em>strategy</em>
/// varies independently of the document backend, so an LLM-backed extractor
/// is a third adapter implementing this interface directly rather than
/// folding a Schema. Register one with
/// <c>ScraperEngineBuilder.WithContentExtractor</c> (ADR-0039).
/// </para>
/// <para>
/// The terminal projection is <see cref="JsonObject"/> — the AOT-clean,
/// Newtonsoft-free record shape (ADR-0008) every
/// <see cref="WebReaper.Sinks.Models.ParsedData"/> and <c>IScraperSink</c>
/// consumes.
/// </para>
/// </summary>
public interface IContentExtractor
{
    /// <summary>
    /// Extract the structured record from <paramref name="document"/> — one
    /// loaded target page — and project it to a <see cref="JsonObject"/>.
    /// <paramref name="schema"/> declares what to extract <em>for the
    /// Schema-driven strategy</em> (the deterministic
    /// <see cref="Concrete.SchemaFold{TNode}"/>); other strategies define
    /// their own use of it:
    /// <list type="bullet">
    /// <item><see cref="Concrete.SchemaFold{TNode}"/> requires a non-null
    /// <paramref name="schema"/> and throws
    /// <see cref="System.ArgumentNullException"/> otherwise (a crawl that
    /// reached this fold carries one by construction — ADR-0025 /
    /// ADR-0040).</item>
    /// <item><see cref="Concrete.MarkdownContentExtractor"/> accepts and
    /// ignores <paramref name="schema"/> — the Markdown strategy has no
    /// place to project a Schema onto (ADR-0040).</item>
    /// <item>An LLM-backed extractor (planned) consumes
    /// <paramref name="schema"/> as the structured-output spec it asks the
    /// model for.</item>
    /// </list>
    /// The seam's nullability is therefore strategy-local; the doc widening
    /// (ADR-0040 §3) is the prose catching up to that variation.
    /// </summary>
    Task<JsonObject> ExtractAsync(string document, Schema? schema);
}
