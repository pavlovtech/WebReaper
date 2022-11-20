using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Immutable;
using Exoscan.Domain.Parsing;
using Exoscan.Domain.Selectors;
using Exoscan.PageActions;

namespace Exoscan.Core.Builders;

public class ConfigBuilder
{
    private readonly List<LinkPathSelector> _linkPathSelectors = new();

    private string _startUrl;

    private Schema? _schema;
    
    private PageType _startPageType;

    private List<PageAction>? _pageActions = null;

    public ConfigBuilder Get(string startUrl)
    {
        _startUrl = startUrl;
        _startPageType = PageType.Static;

        return this;
    }

    public ConfigBuilder GetWithBrowser(
        string startUrl,
        List<PageAction>? pageActions = null)
    {
        _startUrl = startUrl;

        _startPageType = PageType.Dynamic;
        _pageActions = pageActions;

        return this;
    }

    public ConfigBuilder Follow(string linkSelector)
    {
        _linkPathSelectors.Add(new LinkPathSelector(linkSelector, null, PageType.Static));
        return this;
    }

    public ConfigBuilder FollowWithBrowser(
        string linkSelector,
        List<PageAction>? pageActions = null)
    {
        _linkPathSelectors.Add(new LinkPathSelector(linkSelector, null, PageType.Dynamic, pageActions));
        return this;
    }

    public ConfigBuilder Paginate(
        string linkSelector,
        string paginationSelector)
    {
        _linkPathSelectors.Add(new LinkPathSelector(linkSelector, paginationSelector, PageType.Static));
        return this;
    }

    public ConfigBuilder PaginateWithBrowser(
        string linkSelector,
        string paginationSelector,
        List<PageAction>? pageActions = null)
    {
        _linkPathSelectors.Add(new LinkPathSelector(linkSelector, paginationSelector, PageType.Dynamic, pageActions));
        return this;
    }

    public ConfigBuilder WithScheme(Schema schema)
    {
        _schema = schema;
        return this;
    }

    public ScraperConfig Build()
    {
        ArgumentNullException.ThrowIfNull(_startUrl);
        ArgumentNullException.ThrowIfNull(_schema);

        return new ScraperConfig(_schema, ImmutableQueue.Create(_linkPathSelectors.ToArray()), _startUrl, _startPageType, _pageActions);
    }
}