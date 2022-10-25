using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Domain.Selectors;
using WebReaper.Domain.Parsing;
using System.Collections.Immutable;

namespace WebReaper.Core;

public class ScraperConfigBuilder
{
    private readonly List<LinkPathSelector> _linkPathSelectors = new();

    private string _startUrl;

    private Schema? _schema;

    protected ILogger Logger = NullLogger.Instance;
    private PageType _startPageType;
    private string? _initialScript;

    public ScraperConfigBuilder WithStartUrl(
        string startUrl,
        PageType pageType = PageType.Static,
        string? initScript = null)
    {
        _startUrl = startUrl;

        _startPageType = pageType;
        _initialScript = initScript;

        return this;
    }

    public ScraperConfigBuilder FollowLinks(
        string linkSelector,
        PageType pageType = PageType.Static,
        string? script = null)
    {
        _linkPathSelectors.Add(new LinkPathSelector(linkSelector, null, pageType, script));
        return this;
    }

    public ScraperConfigBuilder FollowLinks(
        string linkSelector,
        string paginationSelector,
        PageType pageType = PageType.Static,
        string? script = null)
    {
        _linkPathSelectors.Add(new LinkPathSelector(linkSelector, paginationSelector, pageType, script));
        return this;
    }

    public ScraperConfigBuilder WithScheme(Schema schema)
    {
        _schema = schema;
        return this;
    }

    public ScraperConfig Build()
    {
        ArgumentNullException.ThrowIfNull(_startUrl);
        ArgumentNullException.ThrowIfNull(_schema);
        return new ScraperConfig(_schema, ImmutableQueue.Create(_linkPathSelectors.ToArray()), _startUrl, _startPageType, _initialScript);
    }
}