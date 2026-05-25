using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using WebReaper.AI.Llm;
using WebReaper.Domain.Parsing;

namespace WebReaper.AI.Tests;

// ADR-0067: the LLM adapter of the ISchemaInferrer seam. Uses a stub
// IChatClient that returns a canned response — no real model call — to
// pin the prompt composition, response-shape parsing (both wrapped and
// bare), goal threading, Markdown pre-clean default, content
// truncation, descriptor wiring (name + cache policy), and telemetry
// attribution.
public class LlmSchemaInferrerTests
{
    [Fact]
    public async Task Returns_schema_built_from_wrapped_fields_object()
    {
        var chat = new StubChatClient(_ =>
            "{\"fields\":{\"title\":\"h1\",\"price\":\".price\"}}");

        var schema = await new LlmSchemaInferrer(chat).InferAsync(
            "<article><h1>Hello</h1><span class=\"price\">$10</span></article>");

        Assert.Equal(2, schema.Children.Count);
        var titleElement = Assert.IsType<SchemaElement>(schema.Children[0]);
        Assert.Equal("title", titleElement.Field);
        Assert.Equal("h1", titleElement.Selector);
        var priceElement = Assert.IsType<SchemaElement>(schema.Children[1]);
        Assert.Equal("price", priceElement.Field);
        Assert.Equal(".price", priceElement.Selector);
    }

    [Fact]
    public async Task Accepts_bare_field_map_without_fields_wrapper()
    {
        // Belt-and-braces: a model that returns the flat shape instead
        // of the descriptor-requested wrapped shape is honoured without
        // a parse retry.
        var chat = new StubChatClient(_ => "{\"title\":\"h1\"}");

        var schema = await new LlmSchemaInferrer(chat).InferAsync("<x/>");

        Assert.Single(schema.Children);
        var element = Assert.IsType<SchemaElement>(schema.Children[0]);
        Assert.Equal("title", element.Field);
        Assert.Equal("h1", element.Selector);
    }

    [Fact]
    public async Task Accepts_nested_selector_object_shape()
    {
        // Some models embellish: { "title": { "selector": "h1" } }.
        // The flat-shape parser handles both nested-selector and direct-
        // string values without a retry.
        var chat = new StubChatClient(_ =>
            "{\"fields\":{\"title\":{\"selector\":\"h1\"}}}");

        var schema = await new LlmSchemaInferrer(chat).InferAsync("<x/>");

        Assert.Single(schema.Children);
        var element = Assert.IsType<SchemaElement>(schema.Children[0]);
        Assert.Equal("title", element.Field);
        Assert.Equal("h1", element.Selector);
    }

    [Fact]
    public async Task Strips_markdown_code_fences_from_response()
    {
        var chat = new StubChatClient(_ =>
            "```json\n{\"fields\":{\"title\":\"h1\"}}\n```");

        var schema = await new LlmSchemaInferrer(chat).InferAsync("<x/>");

        Assert.Single(schema.Children);
    }

    [Fact]
    public async Task Throws_when_response_has_no_usable_selectors()
    {
        var chat = new StubChatClient(_ => "{\"fields\":{}}");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new LlmSchemaInferrer(chat).InferAsync("<x/>"));
    }

    [Fact]
    public async Task Throws_when_response_is_not_a_json_object()
    {
        var chat = new StubChatClient(_ => "\"a string\"");

        await Assert.ThrowsAsync<LlmCallException>(
            () => new LlmSchemaInferrer(chat).InferAsync("<x/>"));
    }

    [Fact]
    public async Task Goal_threads_into_the_user_prompt_when_supplied()
    {
        string? capturedUser = null;
        var chat = new StubChatClient(messages =>
        {
            capturedUser = messages
                .Where(m => m.Role == ChatRole.User)
                .Select(m => m.Text)
                .FirstOrDefault();
            return "{\"fields\":{\"title\":\"h1\"}}";
        });

        await new LlmSchemaInferrer(chat).InferAsync(
            "<article><h1>x</h1></article>",
            goal: "product details");

        Assert.NotNull(capturedUser);
        Assert.Contains("Goal: product details", capturedUser!);
    }

    [Fact]
    public async Task Null_goal_omits_the_goal_section()
    {
        string? capturedUser = null;
        var chat = new StubChatClient(messages =>
        {
            capturedUser = messages
                .Where(m => m.Role == ChatRole.User)
                .Select(m => m.Text)
                .FirstOrDefault();
            return "{\"fields\":{\"title\":\"h1\"}}";
        });

        await new LlmSchemaInferrer(chat).InferAsync(
            "<article><h1>x</h1></article>", goal: null);

        Assert.NotNull(capturedUser);
        Assert.DoesNotContain("Goal:", capturedUser!);
    }

    [Fact]
    public async Task Markdown_pre_clean_default_strips_html_chrome()
    {
        string? capturedUser = null;
        var chat = new StubChatClient(messages =>
        {
            capturedUser = messages
                .Where(m => m.Role == ChatRole.User)
                .Select(m => m.Text)
                .FirstOrDefault();
            return "{\"fields\":{\"title\":\"h1\"}}";
        });

        await new LlmSchemaInferrer(chat).InferAsync(
            "<article><h1>Hello</h1><nav>menu</nav></article>");

        Assert.NotNull(capturedUser);
        // <nav> stripped by Markdown pre-clean; the H1 survives in
        // the cleaned content.
        Assert.Contains("Hello", capturedUser!);
        Assert.DoesNotContain("<nav>", capturedUser!);
        Assert.DoesNotContain("<article>", capturedUser!);
    }

    [Fact]
    public async Task UseMarkdownPreClean_false_sends_raw_html()
    {
        string? capturedUser = null;
        var chat = new StubChatClient(messages =>
        {
            capturedUser = messages
                .Where(m => m.Role == ChatRole.User)
                .Select(m => m.Text)
                .FirstOrDefault();
            return "{\"fields\":{\"title\":\"h1\"}}";
        });

        var options = new LlmSchemaInferrerOptions(UseMarkdownPreClean: false);
        await new LlmSchemaInferrer(chat, options).InferAsync(
            "<article><h1>Hello</h1></article>");

        Assert.NotNull(capturedUser);
        // Raw HTML literally in the prompt.
        Assert.Contains("<article>", capturedUser!);
        Assert.Contains("<h1>", capturedUser!);
    }

    [Fact]
    public async Task MaxContentChars_truncates_long_content()
    {
        string? capturedUser = null;
        var chat = new StubChatClient(messages =>
        {
            capturedUser = messages
                .Where(m => m.Role == ChatRole.User)
                .Select(m => m.Text)
                .FirstOrDefault();
            return "{\"fields\":{\"title\":\"h1\"}}";
        });

        var bigContent = "<article><h1>X</h1>" + new string('a', 100_000) + "</article>";
        var options = new LlmSchemaInferrerOptions(MaxContentChars: 256);

        await new LlmSchemaInferrer(chat, options).InferAsync(bigContent);

        Assert.NotNull(capturedUser);
        // 256 chars of page content + the goal-omitted prompt scaffolding
        // — well under 1024 even with the trailing instruction.
        Assert.True(capturedUser!.Length < 1024,
            $"Expected user prompt under 1024 chars after truncation; was {capturedUser.Length}");
    }

    [Fact]
    public async Task Custom_system_prompt_overrides_default()
    {
        ChatMessage? capturedSystem = null;
        var chat = new StubChatClient(messages =>
        {
            capturedSystem = messages.FirstOrDefault(m => m.Role == ChatRole.System);
            return "{\"fields\":{\"title\":\"h1\"}}";
        });

        var options = new LlmSchemaInferrerOptions(
            SystemPrompt: "CUSTOM INFERENCE PROMPT");
        await new LlmSchemaInferrer(chat, options).InferAsync("<x/>");

        Assert.NotNull(capturedSystem);
        Assert.Equal("CUSTOM INFERENCE PROMPT", capturedSystem!.Text);
    }

    [Fact]
    public async Task Telemetry_attributes_calls_to_the_adapter_name()
    {
        var telemetry = new LlmCallTelemetry();
        var chat = new StubChatClient(_ => "{\"fields\":{\"title\":\"h1\"}}");

        var inferrer = new LlmSchemaInferrer(chat, options: null, telemetry: telemetry);
        await inferrer.InferAsync("<article><h1>x</h1></article>");

        var snap = telemetry.Snapshot();
        Assert.Equal(1, snap.CallCount);
        Assert.True(snap.PerAdapter.ContainsKey(nameof(LlmSchemaInferrer)),
            $"Expected '{nameof(LlmSchemaInferrer)}' in PerAdapter; got: " +
            string.Join(", ", snap.PerAdapter.Keys));
    }

    [Fact]
    public async Task Null_telemetry_does_not_throw()
    {
        var chat = new StubChatClient(_ => "{\"fields\":{\"title\":\"h1\"}}");
        var inferrer = new LlmSchemaInferrer(chat, options: null, telemetry: null);

        await inferrer.InferAsync("<article><h1>x</h1></article>");
    }

    [Fact]
    public async Task Options_flow_into_chat_options()
    {
        ChatOptions? captured = null;
        var chat = new StubChatClient((_, opts) =>
        {
            captured = opts;
            return "{\"fields\":{\"title\":\"h1\"}}";
        });

        var options = new LlmSchemaInferrerOptions(
            Model: "gpt-test",
            Temperature: 0.3f,
            MaxResponseTokens: 2048);

        await new LlmSchemaInferrer(chat, options).InferAsync("<x/>");

        Assert.NotNull(captured);
        Assert.Equal("gpt-test", captured!.ModelId);
        Assert.Equal(0.3f, captured.Temperature);
        Assert.Equal(2048, captured.MaxOutputTokens);
        Assert.NotNull(captured.ResponseFormat);
    }

    [Fact]
    public async Task Default_cache_policy_is_Default_not_Hinted()
    {
        // ADR-0065: single-page inference is one-shot per crawl — the
        // cache-write premium doesn't amortise. Default policy is
        // Default, not Hinted. (Consumers wiring via .UseAi(...) flow
        // the global CachePolicy.Hinted, but that's a separate path —
        // not in scope here.)
        ChatMessage? capturedSystem = null;
        var chat = new StubChatClient(messages =>
        {
            capturedSystem = messages.FirstOrDefault(m => m.Role == ChatRole.System);
            return "{\"fields\":{\"title\":\"h1\"}}";
        });

        await new LlmSchemaInferrer(chat).InferAsync("<x/>");

        Assert.NotNull(capturedSystem);
        // CachePolicy.Default → no cache_control hint on the system
        // message's AdditionalProperties.
        var hasCacheHint = capturedSystem!.AdditionalProperties is { } props
            && props.ContainsKey("cache_control");
        Assert.False(hasCacheHint, "Default cache policy should not add the cache_control hint");
    }

    [Fact]
    public async Task Hinted_cache_policy_adds_cache_control_hint()
    {
        ChatMessage? capturedSystem = null;
        var chat = new StubChatClient(messages =>
        {
            capturedSystem = messages.FirstOrDefault(m => m.Role == ChatRole.System);
            return "{\"fields\":{\"title\":\"h1\"}}";
        });

        var options = new LlmSchemaInferrerOptions(CachePolicy: CachePolicy.Hinted);
        await new LlmSchemaInferrer(chat, options).InferAsync("<x/>");

        Assert.NotNull(capturedSystem);
        Assert.NotNull(capturedSystem!.AdditionalProperties);
        Assert.True(capturedSystem.AdditionalProperties!.ContainsKey("cache_control"));
    }

    [Fact]
    public void Constructor_rejects_null_chat_client()
    {
        Assert.Throws<ArgumentNullException>(() => new LlmSchemaInferrer(null!));
    }

    [Fact]
    public async Task InferAsync_rejects_null_document()
    {
        var chat = new StubChatClient(_ => "{\"fields\":{\"title\":\"h1\"}}");
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new LlmSchemaInferrer(chat).InferAsync(null!));
    }

    [Fact]
    public async Task User_prompt_asks_for_the_wrapped_fields_shape()
    {
        // Smoke check on the prompt scaffolding — protects the
        // "respond with { fields: { ... } }" instruction from accidental
        // edits that would break the round-trip with strict-shape models.
        string? capturedUser = null;
        var chat = new StubChatClient(messages =>
        {
            capturedUser = messages
                .Where(m => m.Role == ChatRole.User)
                .Select(m => m.Text)
                .FirstOrDefault();
            return "{\"fields\":{\"title\":\"h1\"}}";
        });

        await new LlmSchemaInferrer(chat).InferAsync("<x/>");

        Assert.NotNull(capturedUser);
        Assert.Contains("\"fields\"", capturedUser!);
        Assert.Contains("css-selector", capturedUser!);
    }

    // Stub IChatClient — same shape as the other satellite tests.
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
