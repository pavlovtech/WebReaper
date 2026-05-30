using Microsoft.Extensions.Logging;
using WebReaper.Core.Loaders.Abstract;
using WebReaper.Domain.Selectors;

namespace WebReaper.Core.Loaders.Concrete;

/// <summary>
/// The one <see cref="IPageLoader"/>: the single home for the load-mode
/// decision the Spider used to make. Dispatches on
/// <see cref="PageRequest.PageType"/> to the HTTP or browser
/// <see cref="IPageLoadTransport"/> (ADR 0004), and runs the
/// <see cref="IPageCache"/> cache-aside flow around the dispatch (ADR-0041 —
/// a no-op with the default <see cref="NullPageCache"/>).
/// </summary>
internal class PageLoader : IPageLoader
{
    private readonly IPageLoadTransport _httpTransport;
    private readonly IPageLoadTransport _browserTransport;
    private readonly IPageCache _cache;
    private readonly ILogger _logger;

    public PageLoader(
        IPageLoadTransport httpTransport,
        IPageLoadTransport browserTransport,
        ILogger logger,
        IPageCache? cache = null)
    {
        _httpTransport = httpTransport;
        _browserTransport = browserTransport;
        _logger = logger;
        // ADR-0041: NullPageCache preserves pre-0041 behaviour exactly when
        // no cache is configured. A null is upgraded here so PageLoader's
        // body is unconditional.
        _cache = cache ?? new NullPageCache();
    }

    public async Task<PageLoadResult> LoadAsync(PageRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Loading {PageType} page {Url}", request.PageType, request.Url);

        // ADR-0041: cache-aside. A hit serves the page without invoking the
        // transport — no network or browser load.
        var cached = await _cache.TryReadAsync(request.Url, request.PageType, cancellationToken);
        if (cached is not null)
        {
            _logger.LogInformation("Page cache hit for {Url}", request.Url);
            return cached;
        }

        var document = request.PageType switch
        {
            PageType.Static => await _httpTransport.LoadAsync(request, cancellationToken),
            PageType.Dynamic => await _browserTransport.LoadAsync(request, cancellationToken),
            _ => throw new NotImplementedException()
        };

        // ADR-0041: a cache-write failure must not fail a Crawl that
        // successfully loaded the page. Log and continue.
        try
        {
            await _cache.WriteAsync(request.Url, request.PageType, document, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Page cache write failed for {Url}", request.Url);
        }

        return document;
    }
}
