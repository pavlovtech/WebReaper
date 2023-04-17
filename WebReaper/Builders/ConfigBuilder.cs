using System.Collections.Immutable;
using WebReaper.Domain;
using WebReaper.Domain.PageActions;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;

namespace WebReaper.Builders;

public class ConfigBuilder
{
    private readonly List<LinkPathSelector> _linkPathSelectors = new();

    private IEnumerable<string> _blockedUrls = Enumerable.Empty<string>();

    private bool _headless = true;

    private List<PageAction>? _pageActions;

    private int _pageCrawlLimit = int.MaxValue;

    private Schema? _schema;

    private PageType _startPageType;

    private IEnumerable<string> _startUrls;

    /// <summary>
    ///     This method can be called only one time to specify urls to start crawling with.
    /// </summary>
    /// <param name="startUrls">Initial urls for crawling</param>
    /// <returns>instance of ConfigBuilder</returns>
    public ConfigBuilder Get(params string[] startUrls)
    {
        _startUrls = startUrls;
        _startPageType = PageType.Static;

        return this;
    }

    /// <summary>
    ///     This method can be called only one time to specify urls to start crawling with.
    /// </summary>
    /// <param name="startUrls">Initial urls for crawling</param>
    /// <param name="pageActions">Actions to perform on the page via a browser</param>
    /// <returns>instance of ConfigBuilder</returns>
    public ConfigBuilder GetWithBrowser(
        IEnumerable<string> startUrls,
        List<PageAction>? pageActions = null)
    {
        _startUrls = startUrls;
        _startPageType = PageType.Dynamic;
        _pageActions = pageActions;

        return this;
    }

    public ConfigBuilder Follow(string linkSelector)
    {
        _linkPathSelectors.Add(new LinkPathSelector(linkSelector));
        return this;
    }

    public ConfigBuilder FollowWithBrowser(
        string linkSelector,
        List<PageAction>? pageActions = null)
    {
        _linkPathSelectors.Add(new LinkPathSelector(linkSelector, null, PageType.Dynamic, pageActions));
        return this;
    }

    public ConfigBuilder HeadlessMode(bool headless)
    {
        _headless = headless;
        return this;
    }
    
    public ConfigBuilder IgnoreUrls(IEnumerable<string> urls)
    {
        _blockedUrls = urls;
        return this;
    }

    public ConfigBuilder WithPageCrawlLimit(int limit)
    {
        _pageCrawlLimit = limit;
        return this;
    }

    public ConfigBuilder Paginate(
        string linkSelector,
        string paginationSelector)
    {
        _linkPathSelectors.Add(new LinkPathSelector(linkSelector, paginationSelector));
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
        if (_startUrls is null)
            throw new InvalidOperationException(
                $"Start Url is missing. You must call the {nameof(Get)} or {nameof(GetWithBrowser)} method");
        if (_schema is null)
            throw new InvalidOperationException(
                $"You must call the {nameof(WithScheme)} method to set the parsing scheme");

        return new ScraperConfig(
            _schema,
            ImmutableQueue.Create(_linkPathSelectors.ToArray()),
            _startUrls,
            _blockedUrls,
            _pageCrawlLimit,
            _startPageType,
            _pageActions,
            _headless);
    }
}