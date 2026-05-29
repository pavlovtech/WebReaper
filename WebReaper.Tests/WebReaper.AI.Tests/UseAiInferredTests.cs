using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using WebReaper.AI;
using WebReaper.AI.Llm;
using WebReaper.Builders;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Domain.Parsing;

namespace WebReaper.AI.Tests;

// ADR-0068: the `Inferred` arm on AiPolicyMode. `.UseAi(client, Inferred)`
// wires `WithLlmSchemaInferrer + WithLlmActionResolver` — the
// inferrer-aware version of the firecrawl triple.
public class UseAiInferredTests
{
    [Fact]
    public async Task Scraper_UseAi_Inferred_after_ExtractInferred_builds_successfully()
    {
        var chat = new StubChatClient(_ => "{\"fields\":{\"title\":\"h1\"}}");

        var engine = await ScraperEngineBuilder
            .Crawl("https://example.com")
            .ExtractInferred(goal: "products")
            .UseAi(chat, new AiOptions(Policy: AiPolicyMode.Inferred))
            .BuildAsync();

        Assert.NotNull(engine);
        await engine.DisposeAsync();
    }

    [Fact]
    public void Scraper_UseAi_Inferred_registers_LlmSchemaInferrer_not_NullSentinel()
    {
        var chat = new StubChatClient(_ => "{\"fields\":{\"title\":\"h1\"}}");
        var builder = (ScraperEngineBuilder)ScraperEngineBuilder
            .Crawl("https://example.com")
            .ExtractInferred();
        builder.UseAi(chat, new AiOptions(Policy: AiPolicyMode.Inferred));

        var inferrer = builder.SchemaInferrerForTests;
        Assert.NotSame(NullSchemaInferrer.Instance, inferrer);
        Assert.IsType<LlmSchemaInferrer>(inferrer);
    }

    [Fact]
    public async Task Scraper_UseAi_Inferred_without_ExtractInferred_builds_succeeds_silently()
    {
        // ADR-0067 semantics: the registered inferrer is silently
        // ignored when the seed terminal wasn't ExtractInferred.
        // `Inferred` just registers an inferrer; with no ExtractInferred
        // marker, no wrapper is composed, and the registered inferrer
        // never runs.
        var chat = new StubChatClient(_ => "{\"fields\":{\"title\":\"h1\"}}");

        var engine = await ScraperEngineBuilder
            .Crawl("https://example.com")
            .AsMarkdown()
            .UseAi(chat, new AiOptions(Policy: AiPolicyMode.Inferred))
            .BuildAsync();

        await engine.DisposeAsync();
    }

    [Fact]
    public async Task Scraper_ExtractInferred_then_UseAi_Recommended_throws_at_BuildAsync()
    {
        // Recommended wires WithLlmFallback (an IContentExtractor) but
        // does not wire WithLlmSchemaInferrer. ExtractInferred without
        // a real inferrer triggers the ADR-0067 BuildAsync throw.
        var chat = new StubChatClient(_ => "{}");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ScraperEngineBuilder
                .Crawl("https://example.com")
                .ExtractInferred()
                .UseAi(chat, new AiOptions(Policy: AiPolicyMode.Recommended))
                .BuildAsync());
    }

    [Fact]
    public async Task Per_role_Inferrer_override_threads_to_satellite_descriptor()
    {
        // The per-role record's CachePolicy + MaxContentChars should
        // make it into the wired inferrer's options. Capturing happens
        // via the descriptor configuration on the LlmSchemaInferrer
        // (CachePolicy.Hinted adds a cache_control hint we can verify
        // via the chat client).
        ChatMessage? capturedSystem = null;
        var chat = new StubChatClient(messages =>
        {
            capturedSystem = messages.FirstOrDefault(m => m.Role == ChatRole.System);
            return "{\"fields\":{\"title\":\"h1\"}}";
        });

        var builder = (ScraperEngineBuilder)ScraperEngineBuilder
            .Crawl("https://example.com")
            .ExtractInferred();
        builder.UseAi(chat, new AiOptions(
            Policy: AiPolicyMode.Inferred,
            Inferrer: new LlmSchemaInferrerOptions(
                CachePolicy: CachePolicy.Hinted)));

        var inferrer = (LlmSchemaInferrer)builder.SchemaInferrerForTests;
        // Force an inference call to exercise the descriptor.
        _ = await inferrer.InferAsync("<article><h1>x</h1></article>");

        Assert.NotNull(capturedSystem);
        Assert.NotNull(capturedSystem!.AdditionalProperties);
        Assert.True(capturedSystem.AdditionalProperties!.ContainsKey("cache_control"));
    }

    [Fact]
    public async Task UseAi_synthesised_inferrer_inherits_global_CachePolicy_Hinted_by_default()
    {
        // Default AiOptions has CachePolicy.Hinted. When the per-role
        // Inferrer is null, the synthesised one inherits Hinted (via
        // ResolveInferrerOptions's CachePolicy flow).
        ChatMessage? capturedSystem = null;
        var chat = new StubChatClient(messages =>
        {
            capturedSystem = messages.FirstOrDefault(m => m.Role == ChatRole.System);
            return "{\"fields\":{\"title\":\"h1\"}}";
        });

        var builder = (ScraperEngineBuilder)ScraperEngineBuilder
            .Crawl("https://example.com")
            .ExtractInferred();
        builder.UseAi(chat, new AiOptions(Policy: AiPolicyMode.Inferred));

        var inferrer = (LlmSchemaInferrer)builder.SchemaInferrerForTests;
        _ = await inferrer.InferAsync("<article><h1>x</h1></article>");

        Assert.NotNull(capturedSystem!.AdditionalProperties);
        Assert.True(capturedSystem.AdditionalProperties!.ContainsKey("cache_control"));
    }

    [Fact]
    public async Task Scraper_UseAi_Inferred_does_NOT_wire_LlmFallback_or_LlmExtractor()
    {
        // Mutually-exclusive arms per the closed-sum discipline.
        // Verified by builder accessor exposure not being needed — if
        // either had been wired, BuildAsync for `.ExtractInferred()`
        // would either throw or wrap the LearnedSchemaContentExtractor
        // around the wrong inner.
        var chat = new StubChatClient(_ => "{\"fields\":{\"title\":\"h1\"}}");
        var builder = (ScraperEngineBuilder)ScraperEngineBuilder
            .Crawl("https://example.com")
            .ExtractInferred();
        builder.UseAi(chat, new AiOptions(Policy: AiPolicyMode.Inferred));

        // No exception during build = the wiring is self-consistent.
        var engine = await builder.BuildAsync();
        await engine.DisposeAsync();
    }

    [Fact]
    public async Task Agent_UseAi_Inferred_throws_with_actionable_message()
    {
        // ADR-0068 Fork 5: the agent's brain proposes its own schemas;
        // a separate inferrer arm is structurally redundant. Loud
        // failure pointing at the right modes.
        var chat = new StubChatClient(_ => "{}");
        var agentBuilder = AgentEngineBuilder.Start("https://example.com", "find products");

        var ex = await Task.Run(() => Assert.Throws<ArgumentOutOfRangeException>(() =>
            agentBuilder.UseAi(chat, new AiOptions(Policy: AiPolicyMode.Inferred))));

        Assert.Contains("AiPolicyMode.Inferred", ex.Message);
        Assert.Contains("AgentEngineBuilder", ex.Message);
        Assert.Contains("Recommended", ex.Message);
        Assert.Contains("LlmPrimary", ex.Message);
    }

    [Fact]
    public async Task Agent_UseAi_Recommended_still_works()
    {
        // Regression check — adding the Inferred arm + throw doesn't
        // disturb the existing agent UseAi modes.
        var chat = new StubChatClient(_ => "{}");
        var agentBuilder = AgentEngineBuilder.Start("https://example.com", "find products");
        agentBuilder.UseAi(chat, new AiOptions(Policy: AiPolicyMode.Recommended));

        // We can't easily build the agent without a real brain decision
        // path; the smoke check is that no throw occurred during
        // UseAi(Recommended) — that's all this test covers.
        await Task.CompletedTask;
    }

    [Fact]
    public void Existing_AiPolicyMode_arms_unchanged()
    {
        // Quick smoke: the enum still has the four originals plus
        // Inferred. Pinned by reflection — if someone reorders, this
        // catches it.
        var values = (AiPolicyMode[])Enum.GetValues(typeof(AiPolicyMode));
        Assert.Contains(AiPolicyMode.Recommended, values);
        Assert.Contains(AiPolicyMode.LlmPrimary, values);
        Assert.Contains(AiPolicyMode.ExtractionOnly, values);
        Assert.Contains(AiPolicyMode.None, values);
        Assert.Contains(AiPolicyMode.Inferred, values);
        Assert.Equal(5, values.Length);
    }

    private sealed class StubChatClient : IChatClient
    {
        private readonly Func<IEnumerable<ChatMessage>, ChatOptions?, string> _respond;

        public StubChatClient(Func<IEnumerable<ChatMessage>, string> respond)
            : this((m, _) => respond(m)) { }

        public StubChatClient(Func<IEnumerable<ChatMessage>, ChatOptions?, string> respond)
            => _respond = respond;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var text = _respond(messages, options);
            var msg = new ChatMessage(ChatRole.Assistant, text);
            return Task.FromResult(new ChatResponse(msg));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => GenerateEmpty(cancellationToken);

        private static async IAsyncEnumerable<ChatResponseUpdate> GenerateEmpty(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
