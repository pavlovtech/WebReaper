using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using WebReaper.AI;
using WebReaper.Builders;

namespace WebReaper.AI.Tests;

// ADR-0069: satellite's per-role re-inference trigger fields on
// LlmSchemaInferrerOptions. WithLlmSchemaInferrer threads the option
// values into the builder via WithSchemaInferenceTriggers — flipping
// the core wrapper's default from "trust-the-cache" to "re-infer after
// 3 consecutive validation failures" (the headline ADR-0069 opt-out
// shape).
public class LlmSchemaInferrerReInferenceOptionsTests
{
    [Fact]
    public void Default_ReInferAfterFailures_is_3()
    {
        var opts = new LlmSchemaInferrerOptions();
        Assert.Equal(3, opts.ReInferAfterFailures);
    }

    [Fact]
    public void Default_MaxReInferencesPerInstance_is_int_MaxValue()
    {
        var opts = new LlmSchemaInferrerOptions();
        Assert.Equal(int.MaxValue, opts.MaxReInferencesPerInstance);
    }

    [Fact]
    public void WithLlmSchemaInferrer_threads_default_options_to_builder_triggers()
    {
        var chat = new StubChatClient(_ => "{\"fields\":{\"title\":\"h1\"}}");
        var builder = (ScraperEngineBuilder)ScraperEngineBuilder
            .Crawl("https://example.com")
            .ExtractInferred();

        builder.WithLlmSchemaInferrer(chat);

        var (reInferAfterFailures, maxReInferences) =
            builder.SchemaInferenceTriggersForTests;
        Assert.Equal(3, reInferAfterFailures);
        Assert.Equal(int.MaxValue, maxReInferences);
    }

    [Fact]
    public void WithLlmSchemaInferrer_threads_custom_options_to_builder_triggers()
    {
        var chat = new StubChatClient(_ => "{\"fields\":{\"title\":\"h1\"}}");
        var builder = (ScraperEngineBuilder)ScraperEngineBuilder
            .Crawl("https://example.com")
            .ExtractInferred();

        builder.WithLlmSchemaInferrer(chat,
            new LlmSchemaInferrerOptions(
                ReInferAfterFailures: 5,
                MaxReInferencesPerInstance: 2));

        var (reInferAfterFailures, maxReInferences) =
            builder.SchemaInferenceTriggersForTests;
        Assert.Equal(5, reInferAfterFailures);
        Assert.Equal(2, maxReInferences);
    }

    [Fact]
    public void Opt_out_via_zero_preserves_trust_the_cache_behaviour()
    {
        var chat = new StubChatClient(_ => "{\"fields\":{\"title\":\"h1\"}}");
        var builder = (ScraperEngineBuilder)ScraperEngineBuilder
            .Crawl("https://example.com")
            .ExtractInferred();

        builder.WithLlmSchemaInferrer(chat,
            new LlmSchemaInferrerOptions(ReInferAfterFailures: 0));

        var (reInferAfterFailures, _) = builder.SchemaInferenceTriggersForTests;
        Assert.Equal(0, reInferAfterFailures);
    }

    [Fact]
    public void Direct_builder_call_overrides_satellite_defaults()
    {
        // Order: satellite WithLlmSchemaInferrer (threads 3 / MaxValue),
        // then consumer's explicit WithSchemaInferenceTriggers wins.
        var chat = new StubChatClient(_ => "{\"fields\":{\"title\":\"h1\"}}");
        var builder = (ScraperEngineBuilder)ScraperEngineBuilder
            .Crawl("https://example.com")
            .ExtractInferred();

        builder
            .WithLlmSchemaInferrer(chat)
            .WithSchemaInferenceTriggers(reInferAfterFailures: 7,
                                          maxReInferencesPerInstance: 4);

        var (reInferAfterFailures, maxReInferences) =
            builder.SchemaInferenceTriggersForTests;
        Assert.Equal(7, reInferAfterFailures);
        Assert.Equal(4, maxReInferences);
    }

    [Fact]
    public void WithSchemaInferenceTriggers_rejects_negative()
    {
        var builder = (ScraperEngineBuilder)ScraperEngineBuilder
            .Crawl("https://example.com")
            .ExtractInferred();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.WithSchemaInferenceTriggers(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.WithSchemaInferenceTriggers(1, -1));
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
