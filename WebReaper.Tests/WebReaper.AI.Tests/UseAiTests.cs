using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using WebReaper.AI;
using WebReaper.Builders;
using WebReaper.Core;
using WebReaper.Core.Actions.Abstract;
using WebReaper.Core.Actions.Concrete;
using WebReaper.Core.Agent.Abstract;
using WebReaper.Core.Agent.Concrete;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Domain.Parsing;

namespace WebReaper.AI.Tests;

// ADR-0064: .UseAi(IChatClient, AiOptions?) — the one-line AI enablement
// extension. Tests cover the four AiPolicyMode arms on both builders, the
// per-role override discipline (global flows down; per-role record wins
// when non-null), and the null-argument guards.
public class UseAiTests
{
    private static readonly IChatClient StubClient = new StubChatClient(_ => "{}");

    // -------------------------------------------------------------------
    // Scraper builder — per-mode wiring pins.
    // -------------------------------------------------------------------

    [Fact]
    public async Task Scraper_Recommended_wires_fallback_extractor_with_self_healing_and_action_resolver()
    {
        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };
        var builder = ScraperEngineBuilder
            .Crawl("https://example.com").Extract(schema)
            .UseAi(StubClient);
        var engine = await builder.BuildAsync();

        var extractor = ReadSpiderExtractor(engine);
        // Recommended composes: SelfHealing(primary=ExtractionRouter(fold, llm), repairer=llm).
        // The outermost wrapper is SelfHealingContentExtractor since WithSelfHealing
        // is the last extractor-side call in the policy.
        var selfHealing = Assert.IsType<SelfHealingContentExtractor>(extractor);
        var inner = ReadPrivateField<IContentExtractor>(selfHealing, "_primary");
        Assert.IsType<ExtractionRouter>(inner);

        // Action resolver: wired on the builder via WithLlmActionResolver
        // — read directly off the builder (the engine itself doesn't expose
        // a path to the resolver in HTTP-only test builds).
        Assert.IsType<LlmActionResolver>(ReadBuilderActionResolver(builder));
    }

    [Fact]
    public async Task Scraper_LlmPrimary_wires_llm_extractor_directly_with_self_healing_and_action_resolver()
    {
        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };
        var builder = ScraperEngineBuilder
            .Crawl("https://example.com").Extract(schema)
            .UseAi(StubClient, new AiOptions(Policy: AiPolicyMode.LlmPrimary));
        var engine = await builder.BuildAsync();

        var extractor = ReadSpiderExtractor(engine);
        // LlmPrimary composes: SelfHealing(primary=LlmContentExtractor, repairer=llm).
        var selfHealing = Assert.IsType<SelfHealingContentExtractor>(extractor);
        var inner = ReadPrivateField<IContentExtractor>(selfHealing, "_primary");
        Assert.IsType<LlmContentExtractor>(inner);

        Assert.IsType<LlmActionResolver>(ReadBuilderActionResolver(builder));
    }

    [Fact]
    public async Task Scraper_ExtractionOnly_wires_fallback_only_no_self_healing_no_action_resolver()
    {
        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };
        var builder = ScraperEngineBuilder
            .Crawl("https://example.com").Extract(schema)
            .UseAi(StubClient, new AiOptions(Policy: AiPolicyMode.ExtractionOnly));
        var engine = await builder.BuildAsync();

        var extractor = ReadSpiderExtractor(engine);
        // ExtractionOnly is fallback only — ExtractionRouter is the outer
        // wrapper, no SelfHealing wrapping.
        Assert.IsType<ExtractionRouter>(extractor);

        // No action resolver wired — builder still holds the null sentinel.
        Assert.Equal("NullActionResolver", ReadBuilderActionResolver(builder).GetType().Name);
    }

    [Fact]
    public async Task Scraper_None_wires_nothing_AI_related()
    {
        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };
        var builder = ScraperEngineBuilder
            .Crawl("https://example.com").Extract(schema)
            .UseAi(StubClient, new AiOptions(Policy: AiPolicyMode.None));
        var engine = await builder.BuildAsync();

        var extractor = ReadSpiderExtractor(engine);
        // None wires nothing — the default fold (SchemaFold<IParentNode>)
        // remains; no LLM-side composition.
        Assert.IsAssignableFrom<IContentExtractor>(extractor);
        Assert.IsNotType<ExtractionRouter>(extractor);
        Assert.IsNotType<SelfHealingContentExtractor>(extractor);
        Assert.IsNotType<LlmContentExtractor>(extractor);

        Assert.Equal("NullActionResolver", ReadBuilderActionResolver(builder).GetType().Name);
    }

    [Fact]
    public async Task Scraper_UseAi_without_options_uses_Recommended_defaults()
    {
        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };
        var builderDefault = ScraperEngineBuilder
            .Crawl("https://example.com").Extract(schema)
            .UseAi(StubClient);
        var engineDefault = await builderDefault.BuildAsync();
        var builderExplicit = ScraperEngineBuilder
            .Crawl("https://example.com").Extract(schema)
            .UseAi(StubClient, new AiOptions(Policy: AiPolicyMode.Recommended));
        var engineExplicit = await builderExplicit.BuildAsync();

        // Same wrapper shape — Recommended is the default.
        Assert.Equal(
            ReadSpiderExtractor(engineDefault).GetType(),
            ReadSpiderExtractor(engineExplicit).GetType());
        Assert.Equal(
            ReadBuilderActionResolver(builderDefault).GetType(),
            ReadBuilderActionResolver(builderExplicit).GetType());
    }

    // -------------------------------------------------------------------
    // Agent builder — per-mode wiring pins.
    // -------------------------------------------------------------------

    [Fact]
    public async Task Agent_Recommended_wires_brain_extractor_and_action_resolver()
    {
        var engine = await AgentEngineBuilder
            .Start("https://example.com", "goal")
            .UseAi(StubClient)
            .BuildAsync();

        Assert.IsType<LlmAgentBrain>(ReadAgentBrain(engine));
        Assert.IsType<LlmContentExtractor>(ReadAgentExtractor(engine));
        Assert.IsType<LlmActionResolver>(ReadAgentResolver(engine));
    }

    [Fact]
    public async Task Agent_LlmPrimary_wires_brain_extractor_and_action_resolver()
    {
        var engine = await AgentEngineBuilder
            .Start("https://example.com", "goal")
            .UseAi(StubClient, new AiOptions(Policy: AiPolicyMode.LlmPrimary))
            .BuildAsync();

        Assert.IsType<LlmAgentBrain>(ReadAgentBrain(engine));
        Assert.IsType<LlmContentExtractor>(ReadAgentExtractor(engine));
        Assert.IsType<LlmActionResolver>(ReadAgentResolver(engine));
    }

    [Fact]
    public async Task Agent_ExtractionOnly_wires_brain_and_extractor_no_action_resolver()
    {
        var engine = await AgentEngineBuilder
            .Start("https://example.com", "goal")
            .UseAi(StubClient, new AiOptions(Policy: AiPolicyMode.ExtractionOnly))
            .BuildAsync();

        Assert.IsType<LlmAgentBrain>(ReadAgentBrain(engine));
        Assert.IsType<LlmContentExtractor>(ReadAgentExtractor(engine));
        Assert.Equal("NullActionResolver", ReadAgentResolver(engine).GetType().Name);
    }

    [Fact]
    public async Task Agent_None_wires_brain_only_nothing_else()
    {
        var engine = await AgentEngineBuilder
            .Start("https://example.com", "goal")
            .UseAi(StubClient, new AiOptions(Policy: AiPolicyMode.None))
            .BuildAsync();

        // Brain is required — wired unconditionally.
        Assert.IsType<LlmAgentBrain>(ReadAgentBrain(engine));
        // Extractor: the default (deterministic fold), NOT the LLM extractor.
        Assert.IsNotType<LlmContentExtractor>(ReadAgentExtractor(engine));
        // Resolver: still the null sentinel.
        Assert.Equal("NullActionResolver", ReadAgentResolver(engine).GetType().Name);
    }

    [Fact]
    public async Task Agent_UseAi_without_options_uses_Recommended_defaults()
    {
        var engineDefault = await AgentEngineBuilder
            .Start("https://example.com", "goal")
            .UseAi(StubClient)
            .BuildAsync();
        var engineExplicit = await AgentEngineBuilder
            .Start("https://example.com", "goal")
            .UseAi(StubClient, new AiOptions(Policy: AiPolicyMode.Recommended))
            .BuildAsync();

        Assert.Equal(ReadAgentBrain(engineDefault).GetType(), ReadAgentBrain(engineExplicit).GetType());
        Assert.Equal(ReadAgentExtractor(engineDefault).GetType(), ReadAgentExtractor(engineExplicit).GetType());
        Assert.Equal(ReadAgentResolver(engineDefault).GetType(), ReadAgentResolver(engineExplicit).GetType());
    }

    // -------------------------------------------------------------------
    // AiOptions per-role override discipline.
    // -------------------------------------------------------------------

    [Fact]
    public void AiOptions_Resolver_inherits_global_defaults_when_per_role_record_is_null()
    {
        var options = new AiOptions(
            Model: "gpt-4o-mini",
            Temperature: 0.3f,
            MaxResponseTokens: 2048,
            MarkdownPreClean: false);

        var extractor = options.ResolveExtractorOptions();
        Assert.Equal("gpt-4o-mini", extractor.Model);
        Assert.Equal(0.3f, extractor.Temperature);
        Assert.Equal(2048, extractor.MaxTokens);
        Assert.False(extractor.UseMarkdownPreClean);

        var resolver = options.ResolveResolverOptions();
        Assert.Equal("gpt-4o-mini", resolver.Model);
        Assert.Equal(0.3f, resolver.Temperature);
        // Resolver caps at min(global, 512).
        Assert.Equal(512, resolver.MaxResponseTokens);

        var brain = options.ResolveBrainOptions();
        Assert.Equal("gpt-4o-mini", brain.Model);
        Assert.Equal(0.3f, brain.Temperature);
        // Brain caps at min(global, 1024) — global is 2048 so brain takes 1024.
        Assert.Equal(1024, brain.MaxResponseTokens);

        var repairer = options.ResolveRepairerOptions();
        Assert.Equal("gpt-4o-mini", repairer.Model);
        Assert.Equal(0.3f, repairer.Temperature);
        Assert.Equal(2048, repairer.MaxTokens);
    }

    [Fact]
    public void AiOptions_per_role_record_overrides_global_when_set()
    {
        var options = new AiOptions(
            Model: "gpt-4o-mini",
            Temperature: 0.0f,
            Brain: new LlmAgentBrainOptions(Model: "gpt-4o", Temperature: 0.7f),
            Extractor: new LlmExtractorOptions(Model: "claude-haiku", Temperature: 0.1f));

        var brain = options.ResolveBrainOptions();
        Assert.Equal("gpt-4o", brain.Model);
        Assert.Equal(0.7f, brain.Temperature);

        var extractor = options.ResolveExtractorOptions();
        Assert.Equal("claude-haiku", extractor.Model);
        Assert.Equal(0.1f, extractor.Temperature);

        // Resolver wasn't overridden — inherits global.
        var resolver = options.ResolveResolverOptions();
        Assert.Equal("gpt-4o-mini", resolver.Model);
        Assert.Equal(0.0f, resolver.Temperature);

        // Repairer wasn't overridden — inherits global.
        var repairer = options.ResolveRepairerOptions();
        Assert.Equal("gpt-4o-mini", repairer.Model);
    }

    [Fact]
    public void AiOptions_per_role_resolver_overrides_global_max_response_tokens()
    {
        var options = new AiOptions(
            MaxResponseTokens: 4096,
            Resolver: new LlmActionResolverOptions(MaxResponseTokens: 256));

        var resolver = options.ResolveResolverOptions();
        // Per-role record wins — 256, not the resolver's capped 512.
        Assert.Equal(256, resolver.MaxResponseTokens);
    }

    [Fact]
    public void AiOptions_defaults_match_documented_globals()
    {
        var options = new AiOptions();
        Assert.Null(options.Model);
        Assert.Equal(0.0f, options.Temperature);
        Assert.Equal(4096, options.MaxResponseTokens);
        Assert.True(options.MarkdownPreClean);
        Assert.Null(options.Extractor);
        Assert.Null(options.Resolver);
        Assert.Null(options.Brain);
        Assert.Null(options.Repairer);
        Assert.Equal(AiPolicyMode.Recommended, options.Policy);
    }

    // -------------------------------------------------------------------
    // Null-argument guards.
    // -------------------------------------------------------------------

    [Fact]
    public void Scraper_UseAi_throws_on_null_builder()
    {
        Assert.Throws<ArgumentNullException>(() =>
            UseAiRegistration.UseAi((ScraperEngineBuilder)null!, StubClient));
    }

    [Fact]
    public void Scraper_UseAi_throws_on_null_client()
    {
        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };
        var builder = ScraperEngineBuilder.Crawl("https://example.com").Extract(schema);
        Assert.Throws<ArgumentNullException>(() => builder.UseAi(null!));
    }

    [Fact]
    public void Agent_UseAi_throws_on_null_builder()
    {
        Assert.Throws<ArgumentNullException>(() =>
            UseAiRegistration.UseAi((AgentEngineBuilder)null!, StubClient));
    }

    [Fact]
    public void Agent_UseAi_throws_on_null_client()
    {
        var builder = AgentEngineBuilder.Start("https://example.com", "goal");
        Assert.Throws<ArgumentNullException>(() => builder.UseAi(null!));
    }

    // -------------------------------------------------------------------
    // Reflection helpers — peek at the constructed engines' private state
    // so the tests pin the wiring shape without modifying core or
    // bloating the public surface for test-only access.
    // -------------------------------------------------------------------

    private static IContentExtractor ReadSpiderExtractor(ScraperEngine engine)
    {
        var spider = ReadPrivateProperty<object>(engine, "Spider");
        var crawlStep = ReadPrivateProperty<object>(spider, "CrawlStep");
        return ReadPrivateField<IContentExtractor>(crawlStep, "_extractor");
    }

    // The scraper builder holds the action resolver in a private field; reading
    // it via reflection avoids the HTTP-only test build's
    // BrowserNotConfiguredPageLoadTransport not carrying the resolver. The
    // builder's _actionResolver field is the authoritative source — it's what
    // the satellite registration extensions write, what the build-time warning
    // checker reads, and what the SpiderBuilder mirrors.
    private static IActionResolver ReadBuilderActionResolver(ScraperEngineBuilder builder)
        => ReadPrivateField<IActionResolver>(builder, "_actionResolver");

    private static IAgentBrain ReadAgentBrain(AgentEngine engine)
        => ReadPrivateField<IAgentBrain>(engine, "_brain");

    private static IContentExtractor ReadAgentExtractor(AgentEngine engine)
        => ReadPrivateField<IContentExtractor>(engine, "_contentExtractor");

    private static IActionResolver ReadAgentResolver(AgentEngine engine)
        => ReadPrivateField<IActionResolver>(engine, "_actionResolver");

    private static T ReadPrivateProperty<T>(object obj, string name)
    {
        var prop = obj.GetType().GetProperty(name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new InvalidOperationException(
                $"Property '{name}' not found on {obj.GetType().FullName}");
        return (T)prop.GetValue(obj)!;
    }

    private static T ReadPrivateField<T>(object obj, string name)
    {
        var field = obj.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new InvalidOperationException(
                $"Field '{name}' not found on {obj.GetType().FullName}");
        return (T)field.GetValue(obj)!;
    }

    private static T? TryReadPrivateField<T>(object obj, string name) where T : class
    {
        var field = obj.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (field is null) return null;
        return (T?)field.GetValue(obj);
    }

    // The NullActionResolver type is internal to the core; reach its
    // singleton via reflection so the test can compare-by-identity without
    // taking a dependency on WebReaper internals.
    private static IActionResolver GetNullActionResolver()
    {
        var type = typeof(IActionResolver).Assembly
            .GetType("WebReaper.Core.Actions.Concrete.NullActionResolver")
            ?? throw new InvalidOperationException("NullActionResolver type not found.");
        var instance = type.GetField("Instance", BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("NullActionResolver.Instance not found.");
        return (IActionResolver)instance.GetValue(null)!;
    }

    // Minimal IChatClient stub — never invoked in these tests (BuildAsync
    // doesn't call the model), but UseAi accepts only non-null clients.
    private sealed class StubChatClient : IChatClient
    {
        private readonly Func<IEnumerable<ChatMessage>, string> _respond;
        public StubChatClient(Func<IEnumerable<ChatMessage>, string> respond) => _respond = respond;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var msg = new ChatMessage(ChatRole.Assistant, _respond(messages));
            return Task.FromResult(new ChatResponse(msg));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Empty(cancellationToken);

        private static async IAsyncEnumerable<ChatResponseUpdate> Empty(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
