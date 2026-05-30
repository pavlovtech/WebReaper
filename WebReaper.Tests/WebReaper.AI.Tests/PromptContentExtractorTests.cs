using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using WebReaper.AI;
using WebReaper.Domain.Parsing;
using Xunit;

namespace WebReaper.AI.Tests;

public class PromptContentExtractorTests
{
    private const string Html = "<html><body><h1>hello</h1></body></html>";

    [Fact]
    public async Task Extracts_json_object_from_the_response()
    {
        var extractor = new PromptContentExtractor(
            new StubChatClient(_ => """{"title":"hi"}"""), "extract the title");

        var record = await extractor.ExtractAsync(Html, schema: null);

        Assert.Equal("hi", record["title"]!.GetValue<string>());
    }

    [Fact]
    public async Task Ignores_the_schema_argument_and_accepts_null()
    {
        var extractor = new PromptContentExtractor(
            new StubChatClient(_ => """{"ok":true}"""), "anything");

        // A non-null schema must not change behaviour, and null must not throw
        // (the contrast to the deterministic fold / LlmContentExtractor).
        var withSchema = await extractor.ExtractAsync(Html, new Schema { new SchemaElement("x", ".x") });
        var withoutSchema = await extractor.ExtractAsync(Html, schema: null);

        Assert.True(withSchema["ok"]!.GetValue<bool>());
        Assert.True(withoutSchema["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Sends_the_instruction_in_the_user_message()
    {
        string? userMessage = null;
        var extractor = new PromptContentExtractor(
            new StubChatClient((messages, _) =>
            {
                userMessage = messages.Last().Text;
                return """{"ok":true}""";
            }),
            "find all C-level executives");

        await extractor.ExtractAsync(Html, schema: null);

        Assert.Contains("find all C-level executives", userMessage);
    }

    [Fact]
    public async Task Requests_json_response_format()
    {
        ChatOptions? captured = null;
        var extractor = new PromptContentExtractor(
            new StubChatClient((_, options) => { captured = options; return "{}"; }), "x");

        await extractor.ExtractAsync(Html, schema: null);

        Assert.IsType<ChatResponseFormatJson>(captured!.ResponseFormat);
    }

    [Fact]
    public async Task Wraps_a_top_level_array_under_items()
    {
        var extractor = new PromptContentExtractor(
            new StubChatClient(_ => """[{"name":"a"},{"name":"b"}]"""), "list names");

        var record = await extractor.ExtractAsync(Html, schema: null);

        var items = Assert.IsType<JsonArray>(record["items"]);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void Throws_on_blank_instruction()
    {
        Assert.Throws<ArgumentException>(() =>
            new PromptContentExtractor(new StubChatClient(_ => "{}"), "   "));
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
            => Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, _respond(messages, options))));

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
