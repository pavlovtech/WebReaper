using Microsoft.Extensions.Logging;
using WebReaper.Core.Loaders.Abstract;
using WebReaper.Domain.Selectors;

namespace WebReaper.Core.Loaders.Concrete;

/// <summary>
/// The one <see cref="IPageLoader"/>: the single home for the load-mode
/// decision the Spider used to make. Dispatches on
/// <see cref="PageRequest.PageType"/> to the HTTP or browser
/// <see cref="IPageLoadTransport"/> (ADR 0004).
/// </summary>
internal class PageLoader : IPageLoader
{
    private readonly IPageLoadTransport _httpTransport;
    private readonly IPageLoadTransport _browserTransport;
    private readonly ILogger _logger;

    public PageLoader(IPageLoadTransport httpTransport, IPageLoadTransport browserTransport, ILogger logger)
    {
        _httpTransport = httpTransport;
        _browserTransport = browserTransport;
        _logger = logger;
    }

    public Task<string> LoadAsync(PageRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Loading {PageType} page {Url}", request.PageType, request.Url);

        return request.PageType switch
        {
            PageType.Static => _httpTransport.LoadAsync(request, cancellationToken),
            PageType.Dynamic => _browserTransport.LoadAsync(request, cancellationToken),
            _ => throw new NotImplementedException()
        };
    }
}
