using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Abstract;

/// <summary>
/// Proposes a <see cref="Schema"/> from a page's document content (ADR-0067).
/// Consumed by <see cref="Concrete.LearnedSchemaContentExtractor"/> when the
/// consumer chose the <c>.ExtractInferred(goal?)</c> seed terminal — the
/// inferrer is called on the first page of the crawl; the proposed schema is
/// cached on the wrapper and reused for every subsequent page (the
/// LLM-as-proposer / deterministic-as-validator wedge applied to schema
/// generation).
/// <para>
/// The default implementation lives in <c>WebReaper.AI</c>
/// (<c>LlmSchemaInferrer</c>); the consumer wires it via
/// <c>ScraperEngineBuilder.WithSchemaInferrer</c> or the satellite's
/// <c>WithLlmSchemaInferrer</c>. Consumer-authored alternatives (heuristic,
/// cached, per-tenant) implement this interface directly without taking an
/// AI dependency.
/// </para>
/// </summary>
public interface ISchemaInferrer
{
    /// <summary>
    /// Infer a <see cref="Schema"/> from the document content.
    /// </summary>
    /// <param name="document">The page's content. May be raw HTML or
    /// pre-cleaned Markdown — the inferrer decides how to prepare it for
    /// the model.</param>
    /// <param name="goal">Optional natural-language hint about what to
    /// extract (<c>"product details"</c>, <c>"job listings"</c>, …). When
    /// null, the inferrer makes its best guess from the page content.</param>
    /// <param name="cancellationToken">Threaded to the underlying chat
    /// client.</param>
    /// <returns>A schema the deterministic fold can apply to this and
    /// subsequent pages.</returns>
    Task<Schema> InferAsync(
        string document,
        string? goal = null,
        CancellationToken cancellationToken = default);
}
