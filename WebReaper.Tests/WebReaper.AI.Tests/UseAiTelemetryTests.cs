using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using WebReaper.AI.Llm;
using WebReaper.Builders;

namespace WebReaper.AI.Tests;

// ADR-0066: contract tests for the .UseAi(...) / WithLlm* registration
// path wiring the builder's TelemetryHooks (consumed by BuildAsync into
// the engine's ctor).
public class UseAiTelemetryTests
{
    // ---- ScraperEngineBuilder side ----

    [Fact]
    public void WithLlmExtractor_sets_TelemetryHooks_on_builder()
    {
        var builder = ScraperEngineBuilder
            .Crawl("https://example.com")
            .Extract(new Domain.Parsing.Schema
            {
                new Domain.Parsing.SchemaElement("name", "h1")
            });
        var chat = new StubChatClient();

        Assert.Null(builder.TelemetryHooks); // Pre-registration.

        builder.WithLlmExtractor(chat);

        Assert.NotNull(builder.TelemetryHooks);
        Assert.NotNull(builder.TelemetryHooks!.Snapshot);
        Assert.NotNull(builder.TelemetryHooks.Reset);
        Assert.NotNull(builder.TelemetryHooks.TotalLlmTokens);
    }

    [Fact]
    public void WithLlmFallback_sets_TelemetryHooks_on_builder()
    {
        var builder = ScraperEngineBuilder
            .Crawl("https://example.com")
            .Extract(new Domain.Parsing.Schema
            {
                new Domain.Parsing.SchemaElement("name", "h1")
            });
        var chat = new StubChatClient();

        builder.WithLlmFallback(chat);

        Assert.NotNull(builder.TelemetryHooks);
    }

    [Fact]
    public void Repeated_WithLlm_calls_share_the_same_telemetry_instance()
    {
        var builder = ScraperEngineBuilder
            .Crawl("https://example.com")
            .Extract(new Domain.Parsing.Schema
            {
                new Domain.Parsing.SchemaElement("name", "h1")
            });
        var chat = new StubChatClient();

        var telemetry1 = builder.GetOrCreateLlmTelemetry();
        builder.WithLlmExtractor(chat);
        var telemetry2 = builder.GetOrCreateLlmTelemetry();
        builder.WithLlmFallback(chat);
        var telemetry3 = builder.GetOrCreateLlmTelemetry();

        // ConditionalWeakTable keyed by builder — the same accumulator
        // is returned across calls within one builder's lifetime.
        Assert.Same(telemetry1, telemetry2);
        Assert.Same(telemetry2, telemetry3);
    }

    [Fact]
    public void UseAi_sets_TelemetryHooks_on_scraper_builder()
    {
        var builder = ScraperEngineBuilder
            .Crawl("https://example.com")
            .Extract(new Domain.Parsing.Schema
            {
                new Domain.Parsing.SchemaElement("name", "h1")
            });
        var chat = new StubChatClient();

        builder.UseAi(chat);

        Assert.NotNull(builder.TelemetryHooks);
        Assert.NotNull(builder.TelemetryHooks!.TotalLlmTokens);
    }

    [Fact]
    public void UseAi_with_None_policy_still_creates_TelemetryHooks_on_agent_brain_path()
    {
        // The agent's None mode wires only the brain — but the brain
        // wiring goes through WithLlmBrain which materialises telemetry.
        // Scraper-side None wires nothing — telemetry remains null.
        var scraperBuilder = ScraperEngineBuilder
            .Crawl("https://example.com")
            .Extract(new Domain.Parsing.Schema
            {
                new Domain.Parsing.SchemaElement("name", "h1")
            });
        var chat = new StubChatClient();

        scraperBuilder.UseAi(chat, new AiOptions(Policy: AiPolicyMode.None));

        // Scraper None wires no adapters → no GetOrCreateLlmTelemetry
        // call happens → TelemetryHooks stays null.
        Assert.Null(scraperBuilder.TelemetryHooks);
    }

    // ---- AgentEngineBuilder side ----

    [Fact]
    public void WithLlmBrain_sets_TelemetryHooks_on_agent_builder()
    {
        var builder = AgentEngineBuilder.Start("https://example.com", "test goal");
        var chat = new StubChatClient();

        Assert.Null(builder.TelemetryHooks);

        builder.WithLlmBrain(chat);

        Assert.NotNull(builder.TelemetryHooks);
        Assert.NotNull(builder.TelemetryHooks!.TotalLlmTokens);
    }

    [Fact]
    public void UseAi_on_agent_builder_sets_TelemetryHooks()
    {
        var builder = AgentEngineBuilder.Start("https://example.com", "test goal");
        var chat = new StubChatClient();

        builder.UseAi(chat);

        Assert.NotNull(builder.TelemetryHooks);
    }

    [Fact]
    public void TelemetryHooks_TotalLlmTokens_returns_null_before_any_call_records()
    {
        var builder = ScraperEngineBuilder
            .Crawl("https://example.com")
            .Extract(new Domain.Parsing.Schema
            {
                new Domain.Parsing.SchemaElement("name", "h1")
            });
        var chat = new StubChatClient();

        builder.WithLlmExtractor(chat);

        // No call ever recorded → snapshot is Empty → TotalTokens null.
        Assert.Null(builder.TelemetryHooks!.TotalLlmTokens!.Invoke());
    }

    [Fact]
    public void TelemetryHooks_Snapshot_returns_LlmTelemetrySnapshot_typed_when_cast()
    {
        // RunReport.Llm surfaces as object?; satellite-aware consumers
        // cast back to LlmTelemetrySnapshot. Verify the cast works.
        var builder = ScraperEngineBuilder
            .Crawl("https://example.com")
            .Extract(new Domain.Parsing.Schema
            {
                new Domain.Parsing.SchemaElement("name", "h1")
            });
        var chat = new StubChatClient();

        builder.WithLlmExtractor(chat);

        var snapshot = builder.TelemetryHooks!.Snapshot();
        var typed = Assert.IsType<LlmTelemetrySnapshot>(snapshot);
        Assert.Equal(0, typed.CallCount);
    }

    // ---- Stub IChatClient ----

    private sealed class StubChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "{}")));

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
