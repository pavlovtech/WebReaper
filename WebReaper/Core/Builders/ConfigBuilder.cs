using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Domain.Selectors;
using WebReaper.Domain.Parsing;
using System.Collections.Immutable;

namespace WebReaper.Core.Builders;

public class ConfigBuilder
{
    private readonly List<LinkPathSelector> _linkPathSelectors = new();

    private string _startUrl;

    private Schema? _schema;

    protected ILogger Logger = NullLogger.Instance;
    private PageType _startPageType;
    private string? _initialScript;

    public ConfigBuilder Get(
        string startUrl,
        string? script = null)
    {
        _startUrl = startUrl;

        _startPageType = PageType.Static;
        _initialScript = script;

        return this;
    }

    public ConfigBuilder GetWithBrowser(
        string startUrl,
        string? script = null)
    {
        _startUrl = startUrl;

        _startPageType = PageType.Dynamic;
        _initialScript = script;

        return this;
    }

    public ConfigBuilder Follow(
        string linkSelector,
        string? script = null)
    {
        _linkPathSelectors.Add(new LinkPathSelector(linkSelector, null, PageType.Static, script));
        return this;
    }

    public ConfigBuilder FollowWithBrowser(
        string linkSelector,
        string? script = null)
    {
        _linkPathSelectors.Add(new LinkPathSelector(linkSelector, null, PageType.Dynamic, script));
        return this;
    }

    public ConfigBuilder Paginate(
        string linkSelector,
        string paginationSelector,
        string? script = null)
    {
        _linkPathSelectors.Add(new LinkPathSelector(linkSelector, paginationSelector, PageType.Static, script));
        return this;
    }

    public ConfigBuilder PaginateWithBrowser(
        string linkSelector,
        string paginationSelector,
        string? script = null)
    {
        _linkPathSelectors.Add(new LinkPathSelector(linkSelector, paginationSelector, PageType.Dynamic, script));
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

        return new ScraperConfig(_schema, ImmutableQueue.Create(_linkPathSelectors.ToArray()), _startUrl, _startPageType, _initialScript);
    }
}