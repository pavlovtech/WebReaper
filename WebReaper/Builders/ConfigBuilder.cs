using System.Collections.Immutable;
using WebReaper.Domain;
using WebReaper.Domain.PageActions;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;

namespace WebReaper.Builders;

/// <summary>
/// Accumulates the crawl definition and produces the immutable
/// <see cref="ScraperConfig"/> (the build path: <see cref="ScraperEngineBuilder"/>
/// is a façade over this plus the runtime <c>SpiderBuilder</c>). The
/// <see cref="Follow"/> / <see cref="Paginate"/> calls build the
/// <see cref="LinkPathSelector"/> chain that is the crawl's state machine
/// (ADR-0001): chain length decides parse-vs-follow-vs-paginate. Use this
/// directly only when you need a <see cref="ScraperConfig"/> without the
/// engine (e.g. persisting it for the distributed-worker pattern, ADR-0009);
/// most consumers use the <see cref="ScraperEngineBuilder"/> façade. Fluent —
/// every method returns the same instance; <see cref="Build"/> validates and
/// freezes.
/// </summary>
public class ConfigBuilder
{
    private readonly List<LinkPathSelector> _linkPathSelectors = new();

    private IEnumerable<string> _blockedUrls = Enumerable.Empty<string>();

    private bool _headless = true;

    private List<PageAction>? _pageActions;

    private int _pageCrawlLimit = int.MaxValue;

    private bool _stopWhenDrained;

    private Schema? _schema;

    private PageType _startPageType;

    private IEnumerable<string> _startUrls;

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
    /// null/empty/whitespace (fail-fast, 8.0.0).</exception>
    public ConfigBuilder Follow(string linkSelector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(linkSelector);
        _linkPathSelectors.Add(new LinkPathSelector(linkSelector));
        return this;
    }

    /// <summary>
    /// <see cref="Follow"/> where the followed pages load via the headless
    /// browser (optionally running <paramref name="pageActions"/>). Requires
    /// the WebReaper.Puppeteer satellite (ADR-0009).
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="linkSelector"/> is
    /// null/empty/whitespace.</exception>
    public ConfigBuilder FollowWithBrowser(
        string linkSelector,
        List<PageAction>? pageActions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(linkSelector);
        _linkPathSelectors.Add(new LinkPathSelector(linkSelector, null, PageType.Dynamic, pageActions));
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
    /// running forever waiting for new jobs (issue #20). ADR-0022: this is the
    /// Outstanding-work latch's in-memory adapter — it applies to the
    /// in-memory scheduler.
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
    /// null/empty/whitespace.</exception>
    public ConfigBuilder Paginate(
        string linkSelector,
        string paginationSelector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(linkSelector);
        ArgumentException.ThrowIfNullOrWhiteSpace(paginationSelector);
        _linkPathSelectors.Add(new LinkPathSelector(linkSelector, paginationSelector));
        return this;
    }

    /// <summary>
    /// <see cref="Paginate"/> where the paginated pages load via the headless
    /// browser (optionally running <paramref name="pageActions"/>). Requires
    /// the WebReaper.Puppeteer satellite (ADR-0009).
    /// </summary>
    /// <exception cref="ArgumentException">either selector is
    /// null/empty/whitespace.</exception>
    public ConfigBuilder PaginateWithBrowser(
        string linkSelector,
        string paginationSelector,
        List<PageAction>? pageActions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(linkSelector);
        ArgumentException.ThrowIfNullOrWhiteSpace(paginationSelector);
        _linkPathSelectors.Add(new LinkPathSelector(linkSelector, paginationSelector, PageType.Dynamic, pageActions));
        return this;
    }

    /// <summary>
    /// The extraction <see cref="Schema"/> applied to target pages (the
    /// shared fold grammar, ADR-0002). Required: <see cref="Build"/> throws if
    /// it was never set. (Exposed on the façade as
    /// <see cref="ScraperEngineBuilder.Parse"/>.)
    /// </summary>
    public ConfigBuilder WithScheme(Schema schema)
    {
        _schema = schema;
        return this;
    }

    /// <summary>
    /// Validate and produce the immutable <see cref="ScraperConfig"/>. Builder
    /// order matters — configure before calling this.
    /// </summary>
    /// <exception cref="InvalidOperationException">no start URLs (neither
    /// <see cref="Get"/> nor <see cref="GetWithBrowser"/>, or an empty set) or
    /// no <see cref="Schema"/> (<see cref="WithScheme"/>) was set.</exception>
    public ScraperConfig Build()
    {
        if (_startUrls is null || !_startUrls.Any())
            throw new InvalidOperationException(
                $"Start URLs are missing. You must call the {nameof(Get)} or {nameof(GetWithBrowser)} method with at least one URL");
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
            _headless,
            _stopWhenDrained);
    }
}
