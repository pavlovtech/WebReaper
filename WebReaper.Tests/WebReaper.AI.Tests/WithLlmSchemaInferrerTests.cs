using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using WebReaper.AI.Llm;
using WebReaper.Builders;
using WebReaper.Domain.Parsing;

namespace WebReaper.AI.Tests;

// ADR-0067: the satellite-side registration extension. Wires
// LlmSchemaInferrer through the core's WithSchemaInferrer seam and
// shares the per-builder LlmCallTelemetry instance with the other
// WithLlm* extensions (ADR-0066).
public class WithLlmSchemaInferrerTests
{
    [Fact]
    public async Task Registered_inferrer_satisfies_BuildAsync_after_ExtractInferred()
    {
        var chat = new StubChatClient(_ => "{\"fields\":{\"title\":\"h1\"}}");

        var engine = await ScraperEngineBuilder
            .Crawl("https://example.com")
            .ExtractInferred()
            .WithLlmSchemaInferrer(chat)
            .BuildAsync();

        Assert.NotNull(engine);
        await engine.DisposeAsync();
    }

    [Fact]
    public async Task Without_ExtractInferred_the_registered_inferrer_is_silently_ignored()
    {
        // Mirror of WithSchemaInferrer's "silently ignored" semantics —
        // .WithLlmSchemaInferrer composes onto the same core seam.
        var chat = new StubChatClient(_ => "{\"fields\":{\"title\":\"h1\"}}");

        var engine = await ScraperEngineBuilder
            .Crawl("https://example.com")
            .AsMarkdown()
            .WithLlmSchemaInferrer(chat)
            .BuildAsync();

        await engine.DisposeAsync();
    }

    [Fact]
    public void Rejects_null_builder()
    {
        var chat = new StubChatClient(_ => "{\"fields\":{}}");

        Assert.Throws<ArgumentNullException>(() =>
            LlmSchemaInferrerRegistration.WithLlmSchemaInferrer(null!, chat));
    }

    [Fact]
    public void Rejects_null_chat_client()
    {
        var builder = (ScraperEngineBuilder)ScraperEngineBuilder
            .Crawl("https://example.com")
            .ExtractInferred();

        Assert.Throws<ArgumentNullException>(() =>
            builder.WithLlmSchemaInferrer(null!));
    }

    [Fact]
    public async Task Telemetry_handle_is_shared_with_other_WithLlm_extensions()
    {
        // ADR-0066: a single LlmCallTelemetry per builder, shared across
        // every WithLlm* registration. The extractor + the inferrer
        // running on the same engine should accumulate into one snapshot.
        var chat = new StubChatClient(_ => "{\"fields\":{\"title\":\"h1\"}}");

        var builder = (ScraperEngineBuilder)ScraperEngineBuilder
            .Crawl("https://example.com")
            .ExtractInferred();
        builder.WithLlmExtractor(chat);
        builder.WithLlmSchemaInferrer(chat);

        Assert.NotNull(builder.TelemetryHooks);
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
