using System.Text.Json.Nodes;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Abstract;

/// <summary>
/// The content-extraction seam: turn one loaded target page into the
/// structured record the sinks emit — the content half of crawling a page
/// (link discovery is the separate other half, the concrete
/// <c>LinkExtractor</c> function, ADR-0036).
/// <para>
/// One adapter ships in core: <see cref="Concrete.SchemaFold{TNode}"/>, the
/// deterministic <see cref="Schema"/> fold over an
/// <see cref="ISchemaBackend{TNode}"/> (ADR-0002). The seam is named for the
/// task, not that adapter — extraction <em>strategy</em> varies independently
/// of the document backend, so an LLM-backed extractor is a second adapter
/// implementing this interface directly rather than folding a Schema.
/// Register one with <c>ScraperEngineBuilder.WithContentExtractor</c>
/// (ADR-0039).
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
    /// <paramref name="schema"/> declares what to extract; it is required (a
    /// crawl always has one — ADR-0025) and must not be <c>null</c>.
    /// </summary>
    /// <exception cref="System.ArgumentNullException">
    /// <paramref name="schema"/> is <c>null</c>.</exception>
    Task<JsonObject> ExtractAsync(string document, Schema? schema);
}
