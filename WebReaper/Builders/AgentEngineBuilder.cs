using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Core.Actions.Abstract;
using WebReaper.Core.Actions.Concrete;
using WebReaper.Core.Agent.Abstract;
using WebReaper.Core.Agent.Concrete;
using WebReaper.Core.CookieStorage.Concrete;
using WebReaper.Core.LinkTracker.Abstract;
using WebReaper.Core.LinkTracker.Concrete;
using WebReaper.Core.Loaders.Abstract;
using WebReaper.Core.Loaders.Concrete;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Domain.Selectors;
using WebReaper.Processing.Abstract;
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Concrete;

namespace WebReaper.Builders;

/// <summary>
/// Sibling builder to <see cref="ScraperEngineBuilder"/> and
/// <see cref="DistributedSpiderBuilder"/> (the ADR-0009 / ADR-0025 two-seam
/// pattern, third instance) — builds the in-process Agent driver
/// (<see cref="WebReaper.Core.Agent.Concrete.AgentEngine"/>) for ADR-0051's
/// autonomous "give me X about this site" loop.
/// <para>
/// Entry is <see cref="Start(string, string)"/> — static, requires a start
/// URL and a goal string (fork 2 verdict — string, not structured). At
/// minimum a brain must be registered before <see cref="BuildAsync"/> succeeds
/// (the default is <c>NullAgentBrain</c> which throws on first call); every
/// other piece (run store, content extractor, action resolver, visited
/// tracker, sinks, processors) has an in-memory default.
/// </para>
/// <para>
/// Satellite extensions in <c>WebReaper.Redis</c>, <c>WebReaper.Mongo</c>,
/// <c>WebReaper.Sqlite</c>, <c>WebReaper.Cosmos</c> add
/// <c>this AgentEngineBuilder</c> overloads of the <c>WithRedis*</c> /
/// <c>WithMongo*</c> / <c>WithSqlite*</c> / <c>WithCosmos*</c> registration
/// pattern (ADR-0009) so distributed agent state composes with the existing
/// distributed crawl state in lockstep.
/// </para>
/// </summary>
public sealed class AgentEngineBuilder
{
    private readonly string _startUrl;
    private readonly string _goal;

    private IAgentBrain _brain = NullAgentBrain.Instance;
    private IAgentRunStore _runStore = new InMemoryAgentRunStore();
    private IPageLoader? _pageLoader;
    private IContentExtractor? _contentExtractor;
    private ISchemaValidator? _schemaValidator;
    private IActionResolver _actionResolver = NullActionResolver.Instance;
    private IVisitedLinkTracker _visitedTracker = new InMemoryVisitedLinkTracker();
    private readonly List<IScraperSink> _sinks = new();
    private readonly List<IPageProcessor> _processors = new();
    private AgentEngineOptions _options = new();
    private ILogger _logger = NullLogger.Instance;
    private string? _runId;
    private PageType _pageType = PageType.Static;
    private int _historyWindow = 10;
    private int _visitedWindow = 30;
    private int _candidateUrlCap = 50;
    private int _maxPageMarkdownChars = 32_000;

    /// <summary>
    /// Per-run telemetry hooks (ADR-0066). Set by satellites at
    /// <c>WithLlm*</c> / <c>.UseAi(...)</c> time to register a
    /// per-builder telemetry accumulator; consumed by
    /// <see cref="BuildAsync"/> to pass to the constructed engine.
    /// <c>null</c> when no satellite has registered telemetry — the
    /// engine returns
    /// <see cref="WebReaper.Domain.Agent.AgentResult"/>'s <c>Report</c>
    /// with <c>Llm == null</c>, and
    /// <see cref="AgentEngineOptions.MaxBudgetTokens"/> becomes
    /// silently inert (matching the documented behaviour on chat
    /// clients that don't surface usage).
    /// <para>
    /// This is a satellite hook (the ADR-0058 <c>OnTeardown</c>
    /// pattern). Most consumers should not set it directly; the
    /// satellite's <c>WithLlm*</c> / <c>.UseAi(...)</c> extensions do
    /// it for them.
    /// </para>
    /// </summary>
    public Domain.Telemetry.RunTelemetryHooks? TelemetryHooks { get; set; }

    internal AgentEngineBuilder(string startUrl, string goal)
    {
        ArgumentException.ThrowIfNullOrEmpty(startUrl);
        ArgumentException.ThrowIfNullOrEmpty(goal);
        _startUrl = startUrl;
        _goal = goal;
    }

    /// <summary>
    /// Begin building an agent run targeting <paramref name="startUrl"/> with
    /// the natural-language <paramref name="goal"/>. Static, sibling to
    /// <see cref="ScraperEngineBuilder.Crawl(System.Collections.Generic.IEnumerable{string})"/>.
    /// </summary>
    public static AgentEngineBuilder Start(string startUrl, string goal)
        => new(startUrl, goal);

    /// <summary>Register the <see cref="IAgentBrain"/> — required.</summary>
    public AgentEngineBuilder WithBrain(IAgentBrain brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        _brain = brain;
        return this;
    }

    /// <summary>Register a custom <see cref="IAgentRunStore"/>; defaults to
    /// <see cref="InMemoryAgentRunStore"/>.</summary>
    public AgentEngineBuilder WithRunStore(IAgentRunStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _runStore = store;
        return this;
    }

    /// <summary>Persist agent runs as JSON files under
    /// <paramref name="directory"/> — one file per run id. Survives process
    /// restarts; suitable for single-machine resumable agents.</summary>
    public AgentEngineBuilder WithFileAgentRunStore(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        _runStore = new FileAgentRunStore(directory);
        return this;
    }

    /// <summary>Resume the run identified by <paramref name="runId"/> if the
    /// configured <see cref="IAgentRunStore"/> holds a snapshot for it;
    /// otherwise start a fresh run with this id. Default resume semantics
    /// (ADR-0051 fork 8 §Decision §6).</summary>
    public AgentEngineBuilder WithRunId(string runId)
    {
        ArgumentException.ThrowIfNullOrEmpty(runId);
        _runId = runId;
        return this;
    }

    /// <summary>Force a fresh run id (engine generates a new GUID); ignores
    /// any prior <see cref="WithRunId"/> call.</summary>
    public AgentEngineBuilder WithNewRun()
    {
        _runId = null;
        return this;
    }

    /// <summary>Switch to Dynamic page loading (browser-shaped) — requires a
    /// registered Puppeteer / browser page loader. Without one,
    /// <see cref="BuildAsync"/> still succeeds but page loads fail with the
    /// "browser not configured" message.</summary>
    public AgentEngineBuilder WithBrowser()
    {
        _pageType = PageType.Dynamic;
        return this;
    }

    /// <summary>Plug a custom <see cref="IPageLoader"/> — overrides the
    /// default HTTP-only loader.</summary>
    public AgentEngineBuilder WithPageLoader(IPageLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        _pageLoader = loader;
        return this;
    }

    /// <summary>Plug a custom <see cref="IContentExtractor"/> for the brain's
    /// Extract decisions; defaults to the AngleSharp/CSS
    /// <see cref="SchemaFold{TNode}"/>.</summary>
    public AgentEngineBuilder WithContentExtractor(IContentExtractor extractor)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        _contentExtractor = extractor;
        return this;
    }

    /// <summary>
    /// Wrap the currently-registered (or default <c>SchemaFold</c>) extractor
    /// with an <see cref="ExtractionRouter"/> (ADR-0046): on a validation
    /// failure (per the builder-registered <see cref="ISchemaValidator"/> or
    /// the default <see cref="SchemaSatisfiedValidator"/>) the agent falls
    /// back to <paramref name="fallback"/>. Sibling to
    /// <see cref="ScraperEngineBuilder.WithFallbackExtractor(IContentExtractor)"/>
    /// — same shape on both builders so satellite policy aggregators
    /// (<c>UseAi</c>) wire identically.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="fallback"/> is null.</exception>
    public AgentEngineBuilder WithFallbackExtractor(IContentExtractor fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        var primary = _contentExtractor ?? new SchemaFold<IParentNode>(new AngleSharpSchemaBackend(), _logger);
        _contentExtractor = new ExtractionRouter(primary, fallback, _schemaValidator, _logger);
        return this;
    }

    /// <summary>
    /// Wrap the currently-registered (or default <c>SchemaFold</c>) extractor
    /// with a <see cref="SelfHealingContentExtractor"/> (ADR-0047): on a
    /// failed deterministic pass, ask <paramref name="repairer"/> for a
    /// patched Schema, re-validate, and cache the patch. Sibling to
    /// <see cref="ScraperEngineBuilder.WithSelfHealing(ISelectorRepairer)"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="repairer"/> is null.</exception>
    public AgentEngineBuilder WithSelfHealing(ISelectorRepairer repairer)
    {
        ArgumentNullException.ThrowIfNull(repairer);
        var primary = _contentExtractor ?? new SchemaFold<IParentNode>(new AngleSharpSchemaBackend(), _logger);
        _contentExtractor = new SelfHealingContentExtractor(primary, repairer, _schemaValidator, _logger);
        return this;
    }

    /// <summary>
    /// Register a custom <see cref="ISchemaValidator"/> (ADR-0062) — consulted
    /// by the agent driver after every Extract decision (a failed verdict
    /// becomes <c>AgentDecisionOutcome.Failed("validation: &lt;reason&gt;")</c>
    /// in <c>AgentState.LastOutcome</c>). Also passed to
    /// <see cref="WithFallbackExtractor"/> / <see cref="WithSelfHealing"/>
    /// when those wrappers are composed. Defaults to
    /// <see cref="SchemaSatisfiedValidator"/> (required-leaves-non-empty,
    /// ADR-0029 alignment).
    /// <para>
    /// Call <em>before</em> <see cref="WithFallbackExtractor"/> /
    /// <see cref="WithSelfHealing"/> — the wrapper composes against
    /// whatever validator is registered at that moment.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="validator"/> is null.</exception>
    public AgentEngineBuilder WithSchemaValidator(ISchemaValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        _schemaValidator = validator;
        return this;
    }

    /// <summary>Plug a custom <see cref="IActionResolver"/> — required for
    /// the agent's <see cref="WebReaper.Domain.PageActions.PageAction.SemanticAct"/>
    /// support; defaults to the no-op
    /// <see cref="NullActionResolver"/>.</summary>
    public AgentEngineBuilder WithActionResolver(IActionResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _actionResolver = resolver;
        return this;
    }

    /// <summary>Plug a custom <see cref="IVisitedLinkTracker"/>; defaults to
    /// in-memory.</summary>
    public AgentEngineBuilder WithVisitedLinkTracker(IVisitedLinkTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        _visitedTracker = tracker;
        return this;
    }

    /// <summary>Add an <see cref="IScraperSink"/> to receive every
    /// <see cref="WebReaper.Domain.Agent.AgentDecision.Extract"/> record.</summary>
    public AgentEngineBuilder AddSink(IScraperSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sinks.Add(sink);
        return this;
    }

    /// <summary>Write every extracted record to the console.</summary>
    public AgentEngineBuilder WriteToConsole()
    {
        _sinks.Add(new ConsoleSink());
        return this;
    }

    /// <summary>Write every extracted record to a JSON Lines file.</summary>
    public AgentEngineBuilder WriteToJsonFile(string path, bool dataCleanupOnStart = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _sinks.Add(new JsonLinesFileSink(path, dataCleanupOnStart));
        return this;
    }

    /// <summary>Add an <see cref="IPageProcessor"/> to the page-processor
    /// pipeline that runs on every Extract decision's record before sink
    /// fan-out (ADR-0038, agent path).</summary>
    public AgentEngineBuilder AddProcessor(IPageProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        _processors.Add(processor);
        return this;
    }

    /// <summary>Override the default engine options (caps).</summary>
    public AgentEngineBuilder WithOptions(AgentEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        return this;
    }

    /// <summary>Set the engine's <c>MaxSteps</c> cap; defaults to 50.</summary>
    public AgentEngineBuilder WithMaxSteps(int maxSteps)
    {
        if (maxSteps <= 0) throw new ArgumentOutOfRangeException(nameof(maxSteps));
        _options = _options with { MaxSteps = maxSteps };
        return this;
    }

    /// <summary>Set the engine's <c>MaxBudgetTokens</c> cap; defaults to null
    /// (off).</summary>
    public AgentEngineBuilder WithMaxBudgetTokens(int? maxBudgetTokens)
    {
        _options = _options with { MaxBudgetTokens = maxBudgetTokens };
        return this;
    }

    /// <summary>Plug a logger.</summary>
    public AgentEngineBuilder WithLogger(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        return this;
    }

    /// <summary>Cap the brain's bounded view of decision history (fork 3
    /// verdict); defaults to 10.</summary>
    public AgentEngineBuilder WithHistoryWindow(int n)
    {
        if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
        _historyWindow = n;
        return this;
    }

    /// <summary>Cap the brain's bounded view of visited URLs; defaults
    /// to 30.</summary>
    public AgentEngineBuilder WithVisitedWindow(int n)
    {
        if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
        _visitedWindow = n;
        return this;
    }

    /// <summary>Cap the brain's view of candidate <c>&lt;a href&gt;</c>
    /// URLs per page; defaults to 50.</summary>
    public AgentEngineBuilder WithCandidateUrlCap(int n)
    {
        if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
        _candidateUrlCap = n;
        return this;
    }

    /// <summary>Cap the brain's view of the current page's rendered Markdown
    /// in characters; defaults to 32 000.</summary>
    public AgentEngineBuilder WithMaxPageMarkdownChars(int chars)
    {
        if (chars <= 0) throw new ArgumentOutOfRangeException(nameof(chars));
        _maxPageMarkdownChars = chars;
        return this;
    }

    /// <summary>
    /// Construct the <see cref="AgentEngine"/>. Throws
    /// <see cref="InvalidOperationException"/> when the brain is still the
    /// null default — an agent without a brain is structurally useless
    /// (ADR-0051 §Decision §5).
    /// </summary>
    public Task<AgentEngine> BuildAsync()
    {
        if (ReferenceEquals(_brain, NullAgentBrain.Instance))
            throw new InvalidOperationException(
                "No IAgentBrain registered. Call .WithBrain(brain) — or use the WebReaper.AI satellite's " +
                ".WithLlmBrain(chatClient) extension — before BuildAsync().");

        var loader = _pageLoader ?? BuildDefaultPageLoader();
        var contentExtractor = _contentExtractor ?? new SchemaFold<IParentNode>(new AngleSharpSchemaBackend(), _logger);
        var validator = _schemaValidator ?? SchemaSatisfiedValidator.Instance;

        var engine = new AgentEngine(
            startUrl: _startUrl,
            goal: _goal,
            brain: _brain,
            runStore: _runStore,
            pageLoader: loader,
            contentExtractor: contentExtractor,
            validator: validator,
            actionResolver: _actionResolver,
            visitedTracker: _visitedTracker,
            sinks: _sinks,
            processors: _processors,
            options: _options,
            logger: _logger,
            runId: _runId,
            pageType: _pageType,
            historyWindow: _historyWindow,
            visitedWindow: _visitedWindow,
            candidateUrlCap: _candidateUrlCap,
            maxPageMarkdownChars: _maxPageMarkdownChars,
            // ADR-0066: hand the satellite-registered telemetry hooks to
            // the engine; null when no LLM adapter ran. AgentResult.Report
            // gets the run's accumulated snapshot.
            telemetryHooks: TelemetryHooks);
        return Task.FromResult(engine);
    }

    private IPageLoader BuildDefaultPageLoader()
    {
        // HTTP-only loader; Dynamic-page loads surface the "browser not
        // configured" message. Browser-mode agents plug in their own
        // IPageLoader via .WithPageLoader(...) — typically the
        // WebReaper.Puppeteer satellite's transport.
        var cookieStorage = new InMemoryCookieStorage();
        return new PageLoader(
            new HttpPageLoadTransport(cookieStorage, proxyProvider: null, _logger),
            new BrowserNotConfiguredPageLoadTransport(),
            _logger,
            cache: null);
    }
}
