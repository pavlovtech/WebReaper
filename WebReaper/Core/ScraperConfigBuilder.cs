using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Domain.Selectors;
using WebReaper.Domain.Parsing;
using System.Collections.Immutable;

namespace WebReaper.Core;

public class ScraperConfigBuilder
{
    protected List<LinkPathSelector> linkPathSelectors = new();

    private string? startUrl;

    protected Schema? schema;

    protected ILogger Logger = NullLogger.Instance;
    protected PageType startPageType;
    private string? initialScript;

    public ScraperConfigBuilder WithStartUrl(
        string startUrl,
        PageType pageType = PageType.Static,
        string? initScript = null)
    {
        this.startUrl = startUrl;

        var startUri = new Uri(startUrl);

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
        return new ScraperConfig(schema, ImmutableQueue.Create(linkPathSelectors.ToArray()), startUrl, startPageType, initialScript);
    }
}