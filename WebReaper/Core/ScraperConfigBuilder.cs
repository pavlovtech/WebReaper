using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Domain.Selectors;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core;

public class ScraperConfigBuilder
{
    protected List<LinkPathSelector> linkPathSelectors = new();

    private string? startUrl;

    protected string baseUrl = "";

    protected Schema? schema;

    protected ILogger Logger = NullLogger.Instance;
    protected PageType startPageType;
    private string? initialScript;

    public ScraperConfigBuilder WithLogger(ILogger logger)
    {
        Logger = logger;
        return this;
    }

    public ScraperConfigBuilder WithStartUrl(
        string startUrl,
        PageType pageType = PageType.Static,
        string? initScript = null)
    {
        this.startUrl = startUrl;

        var startUri = new Uri(startUrl);

        this.baseUrl = startUri.ToString();

        startPageType = pageType;
        initialScript = initScript;

        return this;
    }

    public ScraperConfigBuilder FollowLinks(
        string linkSelector,
        PageType pageType = PageType.Static,
        string? script = null)
    {
        linkPathSelectors.Add(new LinkPathSelector(linkSelector, null, pageType, script));
        return this;
    }

    public ScraperConfigBuilder FollowLinks(
        string linkSelector,
        string paginationSelector,
        PageType pageType = PageType.Static,
        string? script = null)
    {
        linkPathSelectors.Add(new LinkPathSelector(linkSelector, paginationSelector, pageType, script));
        return this;
    }

    public ScraperConfigBuilder WithScheme(Schema schema)
    {
        this.schema = schema;
        return this;
    }

    public ScraperConfig Build()
    {
        return new ScraperConfig(schema, linkPathSelectors.ToArray(), startUrl, startPageType, initialScript, baseUrl);
    }
}