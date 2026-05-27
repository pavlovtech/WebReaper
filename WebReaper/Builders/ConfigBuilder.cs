using System.Collections.Immutable;
using WebReaper.Domain;
using WebReaper.Domain.PageActions;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;

namespace WebReaper.Builders;

/// <summary>
/// Internal collaborator of <see cref="ScraperEngineBuilder"/> (ADR-0025):
/// accumulates the crawl definition and produces the immutable
/// <see cref="ScraperConfig"/>. The <see cref="Follow"/> / <see cref="Paginate"/>
/// calls build the <see cref="LinkPathSelector"/> chain that is the crawl's
/// state machine (ADR-0001): chain length decides
/// parse-vs-follow-vs-paginate. Not public — start URLs and the schema are
/// supplied through the staged <c>Crawl(...).Extract(...)</c> entry, so
/// <see cref="Build"/> no longer guards against an unset crawl (the guard
/// became structural). Fluent — every method returns the same instance.
/// </summary>
internal class ConfigBuilder
{
    private readonly List<LinkPathSelector> _linkPathSelectors = new();

    private IEnumerable<string> _blockedUrls = Enumerable.Empty<string>();

    private bool _headless = true;

    private List<PageAction>? _pageActions;

    private int _pageCrawlLimit = int.MaxValue;

    private bool _stopWhenDrained;

    private Schema? _schema;

    private PageType _startPageType;

    // Initialized to an empty seed so the type-system contract holds
    // from construction. The ADR-0025 staged builder guarantees one of
    // Get / GetWithBrowser is called before Build (the seed terminals
    // route through them); the empty default is a structural belt-and-
    // suspenders, never observed at runtime.
    private IEnumerable<string> _startUrls = Array.Empty<string>();

    /// <summary>
    /// The crawl's start URLs, loaded as static HTTP pages. Defines the seed
    /// set and the start <see cref="PageType"/>; call once — a later
    /// <see cref="Get"/> / <see cref="GetWithBrowser"/> replaces the set.
    /// Required: <see cref="Build"/> throws if neither was called (or the set
    /// is empty).
    /// </summary>
    /// <param name="startUrls">Initial URLs for crawling.</param>
    public ConfigBuilder Get(params string[] startUrls)
    {
        _startUrls = startUrls;
        _startPageType = PageType.Static;

        return this;
    }

    /// <summary>
    /// Like <see cref="Get"/>, but the start pages load through the
    /// headless-browser transport (JavaScript-rendered), optionally running
    /// <paramref name="pageActions"/> on each. Requires the WebReaper.Puppeteer
    /// satellite at run time — core is HTTP-only (ADR-0009).
    /// </summary>
    /// <param name="startUrls">Initial URLs for crawling.</param>
    /// <param name="pageActions">Browser actions to perform on each start page
    /// before scraping (see <see cref="PageActionBuilder"/>).</param>
    public ConfigBuilder GetWithBrowser(
        IEnumerable<string> startUrls,
        List<PageAction>? pageActions = null)
    {
        _startUrls = startUrls;
        _startPageType = PageType.Dynamic;
        _pageActions = pageActions;

        return this;
    }

    /// <summary>
    /// Append a follow step: from the current page, enqueue child Jobs for the
    /// links matching <paramref name="linkSelector"/>. Each
    /// <see cref="Follow"/> / <see cref="Paginate"/> adds one selector to the
    /// chain that drives the crawl state machine (ADR-0001).
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="linkSelector"/> is
    /// null/empty/whitespace — enforced at <see cref="LinkPathSelector"/>
    /// construction (ADR-0030).</exception>
    public ConfigBuilder Follow(string linkSelector)
    {
        _linkPathSelectors.Add(LinkPathSelector.Follow(linkSelector));
        return this;
    }

    /// <summary>
    /// <see cref="Follow"/> where the followed pages load via the headless
    /// browser (optionally running <paramref name="pageActions"/>). Requires
    /// the WebReaper.Puppeteer satellite (ADR-0009).
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="linkSelector"/> is
    /// null/empty/whitespace — enforced at <see cref="LinkPathSelector"/>
    /// construction (ADR-0030).</exception>
    public ConfigBuilder FollowWithBrowser(
        string linkSelector,
        List<PageAction>? pageActions = null)
    {
        _linkPathSelectors.Add(
            LinkPathSelector.Follow(linkSelector, PageType.Dynamic, pageActions));
        return this;
    }

    /// <summary>
    /// Run the browser headed (<c>false</c>) or headless (<c>true</c>, the
    /// default) for dynamic pages — headed is useful for debugging a crawl.
    /// </summary>
    public ConfigBuilder HeadlessMode(bool headless)
    {
        _headless = headless;
        return this;
    }

    /// <summary>
    /// URLs the crawl must never enqueue — a blocklist applied during link
    /// discovery.
    /// </summary>
    public ConfigBuilder IgnoreUrls(IEnumerable<string> urls)
    {
        _blockedUrls = urls;
        return this;
    }

    /// <summary>
    /// Soft cap on pages crawled. ADR-0022: the Crawl driver stops once the
    /// visited count reaches <paramref name="limit"/>, but in-flight pages
    /// still finish — the crawl can overshoot by roughly the parallelism
    /// degree. Default: unbounded.
    /// </summary>
    public ConfigBuilder WithPageCrawlLimit(int limit)
    {
        _pageCrawlLimit = limit;
        return this;
    }

    /// <summary>
    /// Stop the engine once every discovered link has been crawled, instead of
    /// running forever waiting for new jobs (issue #20). ADR-0022: the
    /// Outstanding-work latch detects completion; ADR-0037: the Crawl driver
    /// then ends its own consumption of the job stream, so this applies to
    /// every scheduler — in-memory, File, Redis, Sqlite, Azure Service Bus.
    /// </summary>
    public ConfigBuilder StopWhenAllLinksProcessed()
    {
        _stopWhenDrained = true;
        return this;
    }

    /// <summary>
    /// Append a paginate step: apply <paramref name="linkSelector"/> on every
    /// page reached by walking <paramref name="paginationSelector"/> (the
    /// next-page links). A single selector carrying a pagination selector
    /// yields the <c>Paginated</c> outcome (ADR-0001).
    /// </summary>
    /// <exception cref="ArgumentException">either selector is
    /// null/empty/whitespace — enforced at <see cref="LinkPathSelector"/>
    /// construction (ADR-0030).</exception>
    public ConfigBuilder Paginate(
        string linkSelector,
        string paginationSelector)
    {
        _linkPathSelectors.Add(
            LinkPathSelector.Paginate(linkSelector, paginationSelector));
        return this;
    }

    /// <summary>
    /// <see cref="Paginate"/> where the paginated pages load via the headless
    /// browser (optionally running <paramref name="pageActions"/>). Requires
    /// the WebReaper.Puppeteer satellite (ADR-0009).
    /// </summary>
    /// <exception cref="ArgumentException">either selector is
    /// null/empty/whitespace — enforced at <see cref="LinkPathSelector"/>
    /// construction (ADR-0030).</exception>
    public ConfigBuilder PaginateWithBrowser(
        string linkSelector,
        string paginationSelector,
        List<PageAction>? pageActions = null)
    {
        _linkPathSelectors.Add(LinkPathSelector.Paginate(
            linkSelector, paginationSelector, PageType.Dynamic, pageActions));
        return this;
    }

    /// <summary>
    /// The extraction <see cref="Schema"/> applied to target pages (the
    /// shared fold grammar, ADR-0002). Supplied through the staged entry,
    /// <see cref="ICrawlSeed.Extract"/>.
    /// </summary>
    public ConfigBuilder WithScheme(Schema schema)
    {
        _schema = schema;
        return this;
    }

    /// <summary>
    /// Produce the immutable <see cref="ScraperConfig"/>. Start URLs and an
    /// extraction strategy are present by construction (ADR-0025, widened by
    /// ADR-0040): the only way here is <see cref="ScraperEngineBuilder.Crawl(string[])"/>
    /// then a strategy terminal on <see cref="ICrawlSeed"/>
    /// (<see cref="ICrawlSeed.Extract"/> or <see cref="ICrawlSeed.AsMarkdown"/>),
    /// so the old runtime guards are gone. <see cref="ScraperConfig.ParsingScheme"/>
    /// may be null — Markdown extraction (ADR-0040) leaves it unset; the
    /// <see cref="Core.Spider.Concrete.Spider"/> and
    /// <see cref="Core.Crawling.Concrete.CrawlStep"/> are already null-tolerant.
    /// </summary>
    public ScraperConfig Build()
    {
        return new ScraperConfig(
            _schema,
            ImmutableQueue.Create(_linkPathSelectors.ToArray()),
            _startUrls,
            _blockedUrls,
            _pageCrawlLimit,
            _startPageType,
            _pageActions,
            _headless,
            _stopWhenDrained);
    }
}
