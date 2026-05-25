using WebReaper.Core.Parser.Abstract;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Concrete;

/// <summary>
/// The default <see cref="ISchemaInferrer"/> sentinel (ADR-0067). Throws on
/// every call — the builder's <see cref="WebReaper.Builders.ScraperEngineBuilder.BuildAsync"/>
/// detects this via reference identity and throws at build time when
/// <c>.ExtractInferred(...)</c> was used but no real inferrer was registered.
/// Throwing here is the defence-in-depth path for code that constructs a
/// <see cref="LearnedSchemaContentExtractor"/> directly with this sentinel.
/// </summary>
public sealed class NullSchemaInferrer : ISchemaInferrer
{
    /// <summary>The singleton instance — compared by reference identity.</summary>
    public static readonly NullSchemaInferrer Instance = new();

    private NullSchemaInferrer() { }

    /// <inheritdoc/>
    public Task<Schema> InferAsync(
        string document,
        string? goal = null,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(
            "No ISchemaInferrer registered. Call .WithLlmSchemaInferrer(chatClient) " +
            "on the ScraperEngineBuilder before BuildAsync(), or supply a custom " +
            "ISchemaInferrer via .WithSchemaInferrer(inferrer).");
}
