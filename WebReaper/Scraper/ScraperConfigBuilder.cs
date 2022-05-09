using Microsoft.Extensions.Logging;
using WebReaper.Domain.Selectors;
using WebReaper.Domain.Parsing;
using Microsoft.Extensions.Logging.Abstractions;

namespace WebReaper.Scraper;

public class ScraperConfigBuilder
{
    protected List<LinkPathSelector> linkPathSelectors = new();

    private string? startUrl;

    protected string baseUrl = "";

    private Schema? schema;

    protected ILogger Logger = NullLogger.Instance;

    public ScraperConfigBuilder WithLogger(ILogger logger)
    {
        Logger = logger;
        return this;
    }

    public ScraperConfigBuilder WithStartUrl(string startUrl)
    {
        this.startUrl = startUrl;

        var startUri = new Uri(startUrl);

        var baseUrl = startUri.GetLeftPart(UriPartial.Authority);
        var segments = startUri.Segments;

        this.baseUrl = baseUrl + string.Join(string.Empty, segments.SkipLast(1));

        return this;
    }

    public ScraperConfigBuilder FollowLinks(
        string linkSelector,
        SelectorType selectorType = SelectorType.Css,
        PageType pageType = PageType.Static)
    {
        linkPathSelectors.Add(new(linkSelector, SelectorType: selectorType, PageType: pageType));
        return this;
    }

    public ScraperConfigBuilder FollowLinks(string linkSelector, string paginationSelector, SelectorType selectorType = SelectorType.Css, PageType pageType = PageType.Static)
    {
        linkPathSelectors.Add(new(linkSelector, paginationSelector, pageType, selectorType));
        return this;
    }

    public ScraperConfigBuilder WithScheme(Schema schema)
    {
        this.schema = schema;
        return this;
    }

    public ScraperConfig Build()
    {
        return new ScraperConfig(this.schema, this.linkPathSelectors.ToArray(), startUrl, baseUrl);
    }
}