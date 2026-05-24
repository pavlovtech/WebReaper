using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using WebReaper.AI.Llm;

namespace WebReaper.AI.Tests;

// ADR-0059: contract tests for the LlmCall<TResponse> mechanism module
// — the deep module the four AI adapters share. Uses a stub IChatClient
// so we pin every guarantee without a real model.
public class LlmCallTests
{
    // ---- ChatMessage composition --------------------------------------------

    [Fact]
    public async Task System_prompt_is_pinned_as_first_message()
    {
        List<ChatMessage>? captured = null;
        var chat = new StubChatClient((msgs, _) =>
        {
            captured = msgs.ToList();
            return "{}";
        });

        var call = new LlmCall<JsonObject>(chat, MakeDescriptor(
            systemPrompt: "SYS-PROMPT",
            buildUser: i => $"USER:{i}",
            parse: e => new JsonObject()));

        await call.InvokeAsync("input");

        Assert.NotNull(captured);
        Assert.Equal(2, captured!.Count);
        Assert.Equal(ChatRole.System, captured[0].Role);
        Assert.Equal("SYS-PROMPT", captured[0].Text);
        Assert.Equal(ChatRole.User, captured[1].Role);
        Assert.Equal("USER:input", captured[1].Text);
    }

    // ---- ChatOptions plumbing -----------------------------------------------

    [Fact]
    public async Task Applies_temperature_model_and_max_response_tokens()
    {
        ChatOptions? captured = null;
        var chat = new StubChatClient((_, opts) => { captured = opts; return "{}"; });

        var descriptor = MakeDescriptor(
            systemPrompt: "sys",
            buildUser: _ => "u",
            parse: _ => new JsonObject(),
            temperature: 0.42f,
            maxTokens: 999,
            model: "test-model-x");

        var call = new LlmCall<JsonObject>(chat, descriptor);
        await call.InvokeAsync("x");

        Assert.NotNull(captured);
        Assert.Equal(0.42f, captured!.Temperature);
        Assert.Equal(999, captured.MaxOutputTokens);
        Assert.Equal("test-model-x", captured.ModelId);
    }

    [Fact]
    public async Task JsonMode_sets_ResponseFormat_Json_and_no_Tools()
    {
        ChatOptions? captured = null;
        var chat = new StubChatClient((_, opts) => { captured = opts; return "{}"; });

        var call = new LlmCall<JsonObject>(chat, MakeDescriptor(
            systemPrompt: "s", buildUser: _ => "u", parse: _ => new JsonObject()));

        await call.InvokeAsync("x");

        Assert.NotNull(captured);
        Assert.NotNull(captured!.ResponseFormat);
        Assert.Null(captured.Tools);
    }

    [Fact]
    public async Task ToolCall_mode_sets_Tools_and_skips_ResponseFormat()
    {
        ChatOptions? captured = null;
        var chat = new StubChatClient((msgs, opts) =>
        {
            captured = opts;
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent>
            {
                new FunctionCallContent("call-1", "do_thing", new Dictionary<string, object?> { ["x"] = 1 })
            }));
        });

        var tool = new TestAIFunction("do_thing", "A test tool.");

        var descriptor = new LlmCallDescriptor<string>
        {
            Name = "tool-test",
            SystemPrompt = "sys",
            BuildUserMessage = _ => "u",
            ParseResponse = _ => "should-not-be-called",
            Tools = new[] { tool },
            ParseToolCall = (name, args) => name + ":" + args.GetRawText()
        };

        var call = new LlmCall<string>(chat, descriptor);
        var result = await call.InvokeAsync("x");

        Assert.NotNull(captured);
        Assert.NotNull(captured!.Tools);
        Assert.Single(captured.Tools!);
        // Provider implementations may keep ResponseFormat as null when
        // Tools is set (different providers don't allow mixing).
        Assert.Null(captured.ResponseFormat);
        // Parse delegate gets the tool name and the args JSON.
        Assert.StartsWith("do_thing:", result.Value);
        Assert.Contains("\"x\"", result.Value);
    }

    // ---- Code-fence stripping ------------------------------------------------

    [Theory]
    [InlineData("```json\n{\"a\":1}\n```", "{\"a\":1}")]
    [InlineData("```\n{\"a\":1}\n```", "{\"a\":1}")]
    [InlineData("  ```json\n{\"a\":1}\n```  ", "{\"a\":1}")]
    [InlineData("{\"a\":1}", "{\"a\":1}")]
    [InlineData("   {\"a\":1}   ", "{\"a\":1}")]
    public void StripJsonFences_handles_common_shapes(string raw, string expected)
    {
        var stripped = InvokeStripJsonFences(raw);
        Assert.Equal(expected, stripped);
    }

    [Fact]
    public async Task Strips_fences_before_parsing()
    {
        var chat = new StubChatClient(_ => "```json\n{\"k\":\"v\"}\n```");

        var call = new LlmCall<JsonObject>(chat, MakeDescriptor(
            systemPrompt: "s",
            buildUser: _ => "u",
            parse: e => (JsonObject)JsonNode.Parse(e.GetRawText())!));

        var result = await call.InvokeAsync("x");

        Assert.Equal("v", result.Value["k"]!.GetValue<string>());
        Assert.Equal(0, result.ParseRetries);
    }

    // ---- Parse retry --------------------------------------------------------

    [Fact]
    public async Task Retries_once_on_invalid_JSON_with_reminder_appended()
    {
        var calls = 0;
        List<string>? capturedUserMessages = null;
        var chat = new StubChatClient((msgs, _) =>
        {
            calls++;
            capturedUserMessages ??= new List<string>();
            capturedUserMessages.Add(msgs.Single(m => m.Role == ChatRole.User).Text!);
            return calls == 1 ? "{ trailing-comma, }" : "{\"k\":\"v\"}";
        });

        var call = new LlmCall<JsonObject>(chat, MakeDescriptor(
            systemPrompt: "s",
            buildUser: i => $"USER:{i}",
            parse: e => (JsonObject)JsonNode.Parse(e.GetRawText())!));

        var result = await call.InvokeAsync("input");

        Assert.Equal(2, calls);
        Assert.Equal(1, result.ParseRetries);
        Assert.Equal("v", result.Value["k"]!.GetValue<string>());
        Assert.Equal(2, capturedUserMessages!.Count);
        Assert.Equal("USER:input", capturedUserMessages[0]);
        Assert.Contains("USER:input", capturedUserMessages[1]);
        // The reminder is appended on the retry.
        Assert.Contains("valid JSON only", capturedUserMessages[1], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Throws_LlmCallException_after_exhausted_retries()
    {
        var chat = new StubChatClient(_ => "{ still totally not valid json }");

        var call = new LlmCall<JsonObject>(chat, MakeDescriptor(
            systemPrompt: "s",
            buildUser: _ => "u",
            parse: e => (JsonObject)JsonNode.Parse(e.GetRawText())!));

        var ex = await Assert.ThrowsAsync<LlmCallException>(() => call.InvokeAsync("x").AsTask());

        Assert.Equal(2, ex.Attempts);
        Assert.False(string.IsNullOrEmpty(ex.RawResponse));
        Assert.False(string.IsNullOrEmpty(ex.DescriptorName));
    }

    [Fact]
    public async Task Exception_carries_descriptor_name_from_descriptor()
    {
        var chat = new StubChatClient(_ => "not json");

        var call = new LlmCall<JsonObject>(chat, MakeDescriptor(
            systemPrompt: "s",
            buildUser: _ => "u",
            parse: _ => new JsonObject(),
            name: "MyAdapter"));

        var ex = await Assert.ThrowsAsync<LlmCallException>(() => call.InvokeAsync("x").AsTask());
        Assert.Equal("MyAdapter", ex.DescriptorName);
    }

    [Fact]
    public async Task Retries_when_ParseResponse_throws()
    {
        // First parse delegate call throws (model returns valid JSON but
        // the descriptor rejects it); second call returns a valid value.
        var calls = 0;
        var chat = new StubChatClient(_ =>
        {
            calls++;
            return calls == 1 ? "{\"bad\":true}" : "{\"good\":true}";
        });

        var call = new LlmCall<JsonObject>(chat, MakeDescriptor(
            systemPrompt: "s",
            buildUser: _ => "u",
            parse: e =>
            {
                var obj = (JsonObject)JsonNode.Parse(e.GetRawText())!;
                if (obj.ContainsKey("bad"))
                    throw new InvalidOperationException("nope");
                return obj;
            }));

        var result = await call.InvokeAsync("x");

        Assert.Equal(1, result.ParseRetries);
        Assert.True(result.Value.ContainsKey("good"));
    }

    // ---- Usage capture -------------------------------------------------------

    [Fact]
    public async Task Captures_TotalTokens_from_Usage()
    {
        var chat = new StubChatClient((msgs, opts) =>
        {
            var resp = new ChatResponse(new ChatMessage(ChatRole.Assistant, "{\"k\":1}"))
            {
                Usage = new UsageDetails { TotalTokenCount = 42 }
            };
            return resp;
        });

        var call = new LlmCall<JsonObject>(chat, MakeDescriptor(
            systemPrompt: "s", buildUser: _ => "u",
            parse: e => (JsonObject)JsonNode.Parse(e.GetRawText())!));

        var result = await call.InvokeAsync("x");

        Assert.Equal(42L, result.TotalTokens);
    }

    [Fact]
    public async Task TotalTokens_null_when_Usage_absent()
    {
        var chat = new StubChatClient(_ => "{\"k\":1}");

        var call = new LlmCall<JsonObject>(chat, MakeDescriptor(
            systemPrompt: "s", buildUser: _ => "u",
            parse: e => (JsonObject)JsonNode.Parse(e.GetRawText())!));

        var result = await call.InvokeAsync("x");

        Assert.Null(result.TotalTokens);
    }

    [Fact]
    public async Task TotalTokens_accumulates_across_retry()
    {
        var calls = 0;
        var chat = new StubChatClient((msgs, opts) =>
        {
            calls++;
            var body = calls == 1 ? "not json" : "{\"k\":1}";
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, body))
            {
                Usage = new UsageDetails { TotalTokenCount = 10 }
            };
        });

        var call = new LlmCall<JsonObject>(chat, MakeDescriptor(
            systemPrompt: "s", buildUser: _ => "u",
            parse: e => (JsonObject)JsonNode.Parse(e.GetRawText())!));

        var result = await call.InvokeAsync("x");

        // Both calls reported 10 tokens; the result sums them.
        Assert.Equal(20L, result.TotalTokens);
    }

    // ---- Tool-call mode parsing ---------------------------------------------

    [Fact]
    public async Task ToolCall_mode_invokes_ParseToolCall_with_args_JsonElement()
    {
        var chat = new StubChatClient((msgs, opts) =>
            new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent>
            {
                new FunctionCallContent("c1", "fname",
                    new Dictionary<string, object?> { ["selector"] = ".x", ["count"] = 3 })
            })));

        var tool = new TestAIFunction("fname", "x");

        var descriptor = new LlmCallDescriptor<(string, string)>
        {
            Name = "tool",
            SystemPrompt = "s",
            BuildUserMessage = _ => "u",
            ParseResponse = _ => ("from-json", ""),
            Tools = new[] { tool },
            ParseToolCall = (name, args) => (name, args.GetRawText())
        };

        var call = new LlmCall<(string, string)>(chat, descriptor);
        var result = await call.InvokeAsync("x");

        Assert.Equal("fname", result.Value.Item1);
        Assert.Contains("\"selector\"", result.Value.Item2);
        Assert.Contains(".x", result.Value.Item2);
        Assert.Equal(0, result.ParseRetries);
    }

    [Fact]
    public async Task ToolCall_mode_throws_LlmCallException_when_model_omits_tool_call()
    {
        var chat = new StubChatClient(_ => "I refused to call the tool, sorry.");

        var tool = new TestAIFunction("fname", "x");
        var descriptor = new LlmCallDescriptor<string>
        {
            Name = "tool",
            SystemPrompt = "s",
            BuildUserMessage = _ => "u",
            ParseResponse = _ => "ignored",
            Tools = new[] { tool },
            ParseToolCall = (n, a) => n
        };

        var call = new LlmCall<string>(chat, descriptor);

        await Assert.ThrowsAsync<LlmCallException>(() => call.InvokeAsync("x").AsTask());
    }

    [Fact]
    public async Task ToolCall_mode_retries_with_reminder_then_succeeds_on_tool_call()
    {
        // ADR-0060: mechanism retries once when the model returns no
        // FunctionCallContent; the second response with a tool call wins.
        var calls = 0;
        List<string>? userMessages = null;
        var chat = new StubChatClient((msgs, _) =>
        {
            calls++;
            userMessages ??= new List<string>();
            userMessages.Add(msgs.Single(m => m.Role == ChatRole.User).Text!);
            return calls == 1
                ? new ChatResponse(new ChatMessage(ChatRole.Assistant, "no tool here"))
                : new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent>
                {
                    new FunctionCallContent("c2", "fname",
                        new Dictionary<string, object?> { ["x"] = 1 })
                }));
        });

        var tool = new TestAIFunction("fname", "x");
        var descriptor = new LlmCallDescriptor<string>
        {
            Name = "tool",
            SystemPrompt = "s",
            BuildUserMessage = i => $"USER:{i}",
            ParseResponse = _ => "should-not-be-called",
            Tools = new[] { tool },
            ParseToolCall = (n, _) => n
        };

        var call = new LlmCall<string>(chat, descriptor);
        var result = await call.InvokeAsync("input");

        Assert.Equal(2, calls);
        Assert.Equal("fname", result.Value);
        Assert.Equal(1, result.ParseRetries);
        // The reminder is appended on the retry — mentions calling a tool.
        Assert.NotNull(userMessages);
        Assert.Equal(2, userMessages!.Count);
        Assert.Equal("USER:input", userMessages[0]);
        Assert.Contains("tool", userMessages[1], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ToolCall_mode_does_not_invoke_ParseResponse()
    {
        // Structural property: in tool-call mode the descriptor's
        // ParseResponse is bypassed entirely. The brain + resolver pin
        // ParseResponse to a throwing default; this proves the throw
        // never fires.
        var chat = new StubChatClient((msgs, opts) =>
            new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent>
            {
                new FunctionCallContent("c1", "fname",
                    new Dictionary<string, object?> { ["x"] = 1 })
            })));

        var parseResponseCalled = false;
        var tool = new TestAIFunction("fname", "x");
        var descriptor = new LlmCallDescriptor<string>
        {
            Name = "tool",
            SystemPrompt = "s",
            BuildUserMessage = _ => "u",
            ParseResponse = _ => { parseResponseCalled = true; return "should-not-be-called"; },
            Tools = new[] { tool },
            ParseToolCall = (n, _) => n
        };

        var call = new LlmCall<string>(chat, descriptor);
        var result = await call.InvokeAsync("x");

        Assert.False(parseResponseCalled, "ParseResponse must not be called in tool-call mode");
        Assert.Equal("fname", result.Value);
    }

    [Fact]
    public async Task ToolCall_mode_uses_first_FunctionCallContent_and_ignores_subsequent()
    {
        // Fork (multi-tool-per-response v2 deferral): take the first
        // FunctionCallContent and ignore the rest. v1 behaviour.
        var chat = new StubChatClient((msgs, opts) =>
            new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent>
            {
                new FunctionCallContent("c1", "first", new Dictionary<string, object?> { ["a"] = 1 }),
                new FunctionCallContent("c2", "second", new Dictionary<string, object?> { ["b"] = 2 }),
            })));

        var tool = new TestAIFunction("first", "x");
        var descriptor = new LlmCallDescriptor<string>
        {
            Name = "tool",
            SystemPrompt = "s",
            BuildUserMessage = _ => "u",
            ParseResponse = _ => "ignored",
            Tools = new[] { tool },
            ParseToolCall = (n, _) => n
        };

        var call = new LlmCall<string>(chat, descriptor);
        var result = await call.InvokeAsync("x");

        Assert.Equal("first", result.Value);
    }

    [Fact]
    public void Constructor_rejects_Tools_without_ParseToolCall()
    {
        var chat = new StubChatClient(_ => "{}");
        var tool = new TestAIFunction("x", "x");
        var badDescriptor = new LlmCallDescriptor<JsonObject>
        {
            Name = "bad",
            SystemPrompt = "s",
            BuildUserMessage = _ => "u",
            ParseResponse = _ => new JsonObject(),
            Tools = new[] { tool },
            // ParseToolCall left null
        };

        var ex = Assert.Throws<ArgumentException>(() => new LlmCall<JsonObject>(chat, badDescriptor));
        Assert.Contains("ParseToolCall", ex.Message);
    }

    [Fact]
    public void Constructor_rejects_ParseToolCall_without_Tools()
    {
        var chat = new StubChatClient(_ => "{}");
        var badDescriptor = new LlmCallDescriptor<JsonObject>
        {
            Name = "bad",
            SystemPrompt = "s",
            BuildUserMessage = _ => "u",
            ParseResponse = _ => new JsonObject(),
            ParseToolCall = (n, a) => new JsonObject()
        };

        var ex = Assert.Throws<ArgumentException>(() => new LlmCall<JsonObject>(chat, badDescriptor));
        Assert.Contains("Tools", ex.Message);
    }

    // ---- Cancellation -------------------------------------------------------

    [Fact]
    public async Task Cancellation_token_propagates_to_chat_client()
    {
        CancellationToken received = default;
        var chat = new StubChatClient((msgs, opts, ct) =>
        {
            received = ct;
            return "{}";
        });

        var call = new LlmCall<JsonObject>(chat, MakeDescriptor(
            systemPrompt: "s", buildUser: _ => "u",
            parse: e => (JsonObject)JsonNode.Parse(e.GetRawText())!));

        using var cts = new CancellationTokenSource();
        await call.InvokeAsync("x", cts.Token);

        Assert.Equal(cts.Token, received);
    }

    [Fact]
    public async Task Cancellation_thrown_by_chat_propagates()
    {
        var chat = new StubChatClient((msgs, opts, ct) =>
        {
            throw new OperationCanceledException(ct);
        });

        var call = new LlmCall<JsonObject>(chat, MakeDescriptor(
            systemPrompt: "s", buildUser: _ => "u",
            parse: e => (JsonObject)JsonNode.Parse(e.GetRawText())!));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => call.InvokeAsync("x", cts.Token).AsTask());
    }

    // ---- Constructor argument validation ------------------------------------

    [Fact]
    public void Constructor_rejects_null_chat_client()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new LlmCall<JsonObject>(null!, MakeDescriptor(
                systemPrompt: "s", buildUser: _ => "u",
                parse: _ => new JsonObject())));
    }

    [Fact]
    public void Constructor_rejects_null_descriptor()
    {
        var chat = new StubChatClient(_ => "{}");
        Assert.Throws<ArgumentNullException>(() =>
            new LlmCall<JsonObject>(chat, null!));
    }

    [Fact]
    public async Task InvokeAsync_rejects_null_input()
    {
        var chat = new StubChatClient(_ => "{}");
        var call = new LlmCall<JsonObject>(chat, MakeDescriptor(
            systemPrompt: "s", buildUser: _ => "u",
            parse: e => new JsonObject()));

        await Assert.ThrowsAsync<ArgumentNullException>(() => call.InvokeAsync(null!).AsTask());
    }

    // ---- RawResponse field ---------------------------------------------------

    [Fact]
    public async Task RawResponse_carries_the_stripped_JSON_text()
    {
        var chat = new StubChatClient(_ => "```json\n{\"x\":1}\n```");

        var call = new LlmCall<JsonObject>(chat, MakeDescriptor(
            systemPrompt: "s", buildUser: _ => "u",
            parse: e => (JsonObject)JsonNode.Parse(e.GetRawText())!));

        var result = await call.InvokeAsync("x");

        Assert.Equal("{\"x\":1}", result.RawResponse);
    }

    // ---- Helpers -------------------------------------------------------------

    private static LlmCallDescriptor<JsonObject> MakeDescriptor(
        string systemPrompt,
        Func<object, string> buildUser,
        Func<JsonElement, JsonObject> parse,
        float temperature = 0.0f,
        int maxTokens = 4096,
        string? model = null,
        string name = "test") =>
        new()
        {
            Name = name,
            SystemPrompt = systemPrompt,
            BuildUserMessage = buildUser,
            ParseResponse = parse,
            Temperature = temperature,
            MaxResponseTokens = maxTokens,
            Model = model,
        };

    // Reflection access to the internal StripJsonFences for the table-test.
    private static string InvokeStripJsonFences(string raw)
    {
        var mi = typeof(LlmCall<JsonObject>)
            .GetMethod("StripJsonFences",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        return (string)mi.Invoke(null, new object[] { raw })!;
    }

    // Minimal AIFunction subclass for tool-call tests — InvokeCoreAsync is
    // never exercised here (the LlmCall mechanism only sets the function on
    // ChatOptions.Tools; the stub IChatClient fabricates the FunctionCallContent
    // directly), so a noop implementation suffices.
    private sealed class TestAIFunction : AIFunction
    {
        public override string Name { get; }
        public override string Description { get; }

        public TestAIFunction(string name, string description)
        {
            Name = name;
            Description = description;
        }

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
            => ValueTask.FromResult<object?>(null);
    }

    // Stub IChatClient — three overload shapes for convenience:
    //   (msgs)                -> string body
    //   (msgs, opts)          -> string body
    //   (msgs, opts, ct)      -> string body (so the cancellation test can capture ct)
    //   (msgs, opts)          -> ChatResponse (for Usage / FunctionCallContent shapes)
    private sealed class StubChatClient : IChatClient
    {
        private readonly Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, ChatResponse> _respond;

        public StubChatClient(Func<IEnumerable<ChatMessage>, string> respond)
            : this((m, _, _) => StringResp(respond(m))) { }

        public StubChatClient(Func<IEnumerable<ChatMessage>, ChatOptions?, string> respond)
            : this((m, o, _) => StringResp(respond(m, o))) { }

        public StubChatClient(Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, string> respond)
            : this((m, o, c) => StringResp(respond(m, o, c))) { }

        public StubChatClient(Func<IEnumerable<ChatMessage>, ChatOptions?, ChatResponse> respond)
            : this((m, o, _) => respond(m, o)) { }

        private StubChatClient(Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, ChatResponse> respond)
            => _respond = respond;

        private static ChatResponse StringResp(string text)
            => new(new ChatMessage(ChatRole.Assistant, text));

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_respond(messages, options, cancellationToken));

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
