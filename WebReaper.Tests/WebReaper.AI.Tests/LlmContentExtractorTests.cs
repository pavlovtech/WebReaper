using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using WebReaper.AI;
using WebReaper.Domain.Parsing;

namespace WebReaper.AI.Tests;

// ADR-0044: the LLM extractor adapter. Tests use a stub IChatClient that
// returns a canned response — no real model call — so we can pin the
// prompt composition, code-fence stripping, schema-required contract,
// and the JsonObject terminal.
public class LlmContentExtractorTests
{
    [Fact]
    public async Task Returns_parsed_json_object_from_chat_response()
    {
        var chat = new StubChatClient(_ => "{\"title\":\"Hello\"}");

        var schema = new Schema
        {
            new SchemaElement("title", "h1", DataType.String)
        };

        var result = await new LlmContentExtractor(chat).ExtractAsync(
            "<article><h1>Hello</h1></article>", schema);

        Assert.Equal("Hello", result["title"]!.GetValue<string>());
    }

    [Fact]
    public async Task Strips_markdown_code_fences_from_response()
    {
        var chat = new StubChatClient(_ =>
            "```json\n{\"title\":\"Hello\"}\n```");

        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };

        var result = await new LlmContentExtractor(chat).ExtractAsync(
            "<article><h1>Hello</h1></article>", schema);

        Assert.Equal("Hello", result["title"]!.GetValue<string>());
    }

    [Fact]
    public async Task Strips_bare_triple_fences_without_language_tag()
    {
        var chat = new StubChatClient(_ => "```\n{\"title\":\"Hi\"}\n```");

        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };

        var result = await new LlmContentExtractor(chat).ExtractAsync(
            "<article><h1>Hi</h1></article>", schema);

        Assert.Equal("Hi", result["title"]!.GetValue<string>());
    }

    [Fact]
    public async Task Throws_on_non_object_response()
    {
        var chat = new StubChatClient(_ => "\"just a string\"");

        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new LlmContentExtractor(chat).ExtractAsync("<x/>", schema));
    }

    [Fact]
    public async Task Throws_on_null_schema()
    {
        var chat = new StubChatClient(_ => "{}");
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new LlmContentExtractor(chat).ExtractAsync("<x/>", null));
    }

    [Fact]
    public void Constructor_rejects_null_chat_client()
    {
        Assert.Throws<ArgumentNullException>(() => new LlmContentExtractor(null!));
    }

    [Fact]
    public async Task Prompt_includes_schema_and_page_content()
    {
        // The prompt the LLM sees must contain both the JSON Schema
        // and the (cleaned) page content. This is what makes the
        // model do the right thing.
        string? capturedPrompt = null;
        var chat = new StubChatClient(messages =>
        {
            capturedPrompt = string.Join("\n",
                messages.Select(m => m.Text));
            return "{\"title\":\"x\"}";
        });

        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };

        await new LlmContentExtractor(chat).ExtractAsync(
            "<article><h1>Hello world</h1></article>", schema);

        Assert.NotNull(capturedPrompt);
        // Schema shows up in the prompt (the JSON Schema, not the
        // CSS selector — selectors are dropped).
        Assert.Contains("\"title\"", capturedPrompt!);
        Assert.Contains("\"type\":\"object\"", capturedPrompt!);
        Assert.DoesNotContain("h1", capturedPrompt!);
        // Page content shows up — via Markdown pre-clean.
        Assert.Contains("Hello world", capturedPrompt!);
    }

    [Fact]
    public async Task Use_markdown_pre_clean_false_sends_raw_html()
    {
        string? capturedPrompt = null;
        var chat = new StubChatClient(messages =>
        {
            capturedPrompt = string.Join("\n", messages.Select(m => m.Text));
            return "{}";
        });

        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };
        var options = new LlmExtractorOptions(UseMarkdownPreClean: false);

        await new LlmContentExtractor(chat, options).ExtractAsync(
            "<article><h1>Hello</h1></article>", schema);

        // Raw HTML is in the prompt — the literal "<article>" survives,
        // which would be stripped by the Markdown pre-clean.
        Assert.Contains("<article>", capturedPrompt!);
    }

    [Fact]
    public async Task Custom_system_prompt_overrides_default()
    {
        ChatMessage? capturedSystem = null;
        var chat = new StubChatClient(messages =>
        {
            capturedSystem = messages.FirstOrDefault(m => m.Role == ChatRole.System);
            return "{}";
        });

        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };
        var options = new LlmExtractorOptions(SystemPrompt: "CUSTOM SYSTEM PROMPT");

        await new LlmContentExtractor(chat, options).ExtractAsync(
            "<article><h1>x</h1></article>", schema);

        Assert.NotNull(capturedSystem);
        Assert.Equal("CUSTOM SYSTEM PROMPT", capturedSystem!.Text);
    }

    [Fact]
    public async Task Options_flow_into_chat_options()
    {
        ChatOptions? captured = null;
        var chat = new StubChatClient((_, opts) => { captured = opts; return "{}"; });

        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };
        var options = new LlmExtractorOptions(
            Model: "gpt-test",
            Temperature: 0.5f,
            MaxTokens: 1024);

        await new LlmContentExtractor(chat, options).ExtractAsync(
            "<article><h1>x</h1></article>", schema);

        Assert.NotNull(captured);
        Assert.Equal("gpt-test", captured!.ModelId);
        Assert.Equal(0.5f, captured.Temperature);
        Assert.Equal(1024, captured.MaxOutputTokens);
        Assert.NotNull(captured.ResponseFormat);
    }

    // Stub IChatClient — returns a canned string body. Two overloads:
    // one inspects messages only (the common test); one also inspects
    // ChatOptions.
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
