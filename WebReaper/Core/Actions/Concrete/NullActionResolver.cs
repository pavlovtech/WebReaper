using WebReaper.Core.Actions.Abstract;
using WebReaper.Domain.PageActions;

namespace WebReaper.Core.Actions.Concrete;

/// <summary>
/// The no-op default <see cref="IActionResolver"/> (ADR-0050): always returns
/// <c>null</c>. Dispatching a <see cref="PageAction.SemanticAct"/> with this
/// resolver registered throws <see cref="SemanticActResolutionException"/> at
/// the transport. A warning is logged at engine construction
/// (<see cref="WebReaper.Builders.ScraperEngineBuilder.BuildAsync"/>) when the
/// crawl's selector chain contains a <c>SemanticAct</c> and no other resolver
/// has been registered, so the misconfiguration is visible before the crawl
/// starts rather than at the first dispatch.
/// </summary>
internal sealed class NullActionResolver : IActionResolver
{
    /// <summary>The shared singleton — stateless.</summary>
    public static readonly NullActionResolver Instance = new();

    private NullActionResolver() { }

    /// <inheritdoc/>
    public Task<PageAction?> ResolveAsync(
        string intent,
        string pageHtml,
        CancellationToken cancellationToken = default)
        => Task.FromResult<PageAction?>(null);
}
