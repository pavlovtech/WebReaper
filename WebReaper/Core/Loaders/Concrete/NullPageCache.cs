using WebReaper.Core.Loaders.Abstract;
using WebReaper.Domain.Selectors;

namespace WebReaper.Core.Loaders.Concrete;

/// <summary>
/// The default <see cref="IPageCache"/> adapter (ADR-0041): no cache.
/// <see cref="TryReadAsync"/> returns <c>null</c>; <see cref="WriteAsync"/>
/// no-ops. Preserves the pre-0041 PageLoader behaviour exactly when a
/// caller never invokes <c>WithPageCache</c> / <c>WithMaxAge</c>.
/// </summary>
internal sealed class NullPageCache : IPageCache
{
    public Task<string?> TryReadAsync(string url, PageType pageType, CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);

    public Task WriteAsync(string url, PageType pageType, string document, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
