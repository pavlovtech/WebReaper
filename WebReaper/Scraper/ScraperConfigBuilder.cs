using Microsoft.Extensions.Logging;
using WebReaper.Domain.Selectors;
using WebReaper.Domain.Parsing;
using Microsoft.Extensions.Logging.Abstractions;

namespace WebReaper.Core.Scraper;

public class ScraperConfigBuilder
{
    protected List<LinkPathSelector> linkPathSelectors = new();

    private string? startUrl;

    protected string baseUrl = "";

    protected Schema? schema;

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
        SelectorType selectorType = SelectorType.Css)
    {
        linkPathSelectors.Add(new(linkSelector, SelectorType: selectorType));
        return this;
    }

    public ScraperConfigBuilder FollowLinks(string linkSelector, string paginationSelector, SelectorType selectorType = SelectorType.Css)
    {
        linkPathSelectors.Add(new(linkSelector, paginationSelector));
        return this;
    }

    public ScraperConfigBuilder WithScheme(Schema schema)
    {
        this.schema = schema;
        return this;
    }

    public ScraperConfig Build()
    {
        return new ScraperConfig(schema, linkPathSelectors.ToArray(), startUrl, baseUrl);
    }
}