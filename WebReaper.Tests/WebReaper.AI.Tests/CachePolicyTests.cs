using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using WebReaper.AI.Llm;

namespace WebReaper.AI.Tests;

// ADR-0065: contract tests for the LlmCall<TResponse> mechanism's
// system-prompt caching hint encoding + split-usage capture (input /
// output / cached-input / total tokens).
public class CachePolicyTests
{
    // ---- Cache hint encoding ------------------------------------------------

    [Fact]
    public async Task Default_policy_does_not_add_cache_control_to_system_message()
    {
        ChatMessage? systemMessage = null;
        var chat = new StubChatClient((msgs, _) =>
        {
            systemMessage = msgs.First(m => m.Role == ChatRole.System);
            return "{}";
        });

        var call = new LlmCall<JsonObject>(chat, MakeDescriptor(CachePolicy.Default));

        await call.InvokeAsync("x");

        Assert.NotNull(systemMessage);
        Assert.True(systemMessage!.AdditionalProperties is null
            || !systemMessage.AdditionalProperties.ContainsKey("cache_control"));
    }

    [Fact]
    public async Task Hinted_policy_adds_cache_control_ephemeral_to_system_message()
    {
        ChatMessage? systemMessage = null;
        var chat = new StubChatClient((msgs, _) =>
        {
            systemMessage = msgs.First(m => m.Role == ChatRole.System);
            return "{}";
        });

        var call = new LlmCall<JsonObject>(chat, MakeDescriptor(CachePolicy.Hinted));

        await call.InvokeAsync("x");

        Assert.NotNull(systemMessage);
        Assert.NotNull(systemMessage!.AdditionalProperties);
        Assert.True(systemMessage.AdditionalProperties!.ContainsKey("cache_control"));

        // cache_control payload is the Anthropic-standard
        // { "type": "ephemeral" } shape.
        var raw = systemMessage.AdditionalProperties["cache_control"];
        Assert.NotNull(raw);
        var dict = Assert.IsAssignableFrom<IDictionary<string, object?>>(raw);
        Assert.Equal("ephemeral", dict["type"]);
    }

    [Fact]
    public async Task Hinted_policy_does_not_taint_user_message()
    {
        ChatMessage? userMessage = null;
        var chat = new StubChatClient((msgs, _) =>
        {
            userMessage = msgs.First(m => m.Role == ChatRole.User);
            return "{}";
        });

        var call = new LlmCall<JsonObject>(chat, MakeDescriptor(CachePolicy.Hinted));

        await call.InvokeAsync("x");

        Assert.NotNull(userMessage);
        // ADR-0065 v1 caches only the system prompt; the user message
        // must not carry the hint (v2 deferral — user-prefix caching).
        Assert.True(userMessage!.AdditionalProperties is null
            || !userMessage.AdditionalProperties.ContainsKey("cache_control"));
    }

    [Fact]
    public async Task Hinted_policy_attaches_hint_on_every_call_including_retry()
    {
        var systemMessagesObserved = new List<ChatMessage>();
        var calls = 0;
        var chat = new StubChatClient((msgs, _) =>
        {
            calls++;
            systemMessagesObserved.Add(msgs.First(m => m.Role == ChatRole.System));
            return calls == 1 ? "not json" : "{}";
        });

        var call = new LlmCall<JsonObject>(chat, MakeDescriptor(CachePolicy.Hinted));

        await call.InvokeAsync("x");

        Assert.Equal(2, systemMessagesObserved.Count);
        Assert.All(systemMessagesObserved, m =>
        {
            Assert.NotNull(m.AdditionalProperties);
            Assert.True(m.AdditionalProperties!.ContainsKey("cache_control"));
        });
    }

    [Fact]
    public async Task Descriptor_default_SystemPromptCache_is_Default()
    {
        // The descriptor's SystemPromptCache field defaults to
        // CachePolicy.Default — a descriptor that doesn't set it must
        // behave like Default (no hint).
        ChatMessage? systemMessage = null;
        var chat = new StubChatClient((msgs, _) =>
        {
            systemMessage = msgs.First(m => m.Role == ChatRole.System);
            return "{}";
        });

        var call = new LlmCall<JsonObject>(chat, new LlmCallDescriptor<JsonObject>
        {
            Name = "no-cache-set",
            SystemPrompt = "s",
            BuildUserMessage = _ => "u",
            ParseResponse = _ => new JsonObject(),
            // SystemPromptCache deliberately omitted
        });

        await call.InvokeAsync("x");

        Assert.NotNull(systemMessage);
        Assert.True(systemMessage!.AdditionalProperties is null
            || !systemMessage.AdditionalProperties.ContainsKey("cache_control"));
    }

    [Fact]
    public async Task Hinted_policy_works_in_tool_call_mode()
    {
        // The hint goes on the system message regardless of mode —
        // tool-call descriptors must still receive the hint.
        ChatMessage? systemMessage = null;
        var chat = new StubChatClient((msgs, _) =>
        {
            systemMessage = msgs.First(m => m.Role == ChatRole.System);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent>
            {
                new FunctionCallContent("c1", "noop", new Dictionary<string, object?>())
            }));
        });

        var tool = new NoopAIFunction("noop");
        var call = new LlmCall<string>(chat, new LlmCallDescriptor<string>
        {
            Name = "tool-with-cache",
            SystemPrompt = "s",
            BuildUserMessage = _ => "u",
            ParseResponse = _ => "ignored",
            Tools = new[] { tool },
            ParseToolCall = (n, _) => n,
            SystemPromptCache = CachePolicy.Hinted,
        });

        await call.InvokeAsync("x");

        Assert.NotNull(systemMessage);
        Assert.NotNull(systemMessage!.AdditionalProperties);
        Assert.True(systemMessage.AdditionalProperties!.ContainsKey("cache_control"));
    }

    // ---- Split usage capture ------------------------------------------------

    [Fact]
    public async Task Captures_input_output_cached_and_total_from_UsageDetails()
    {
        var chat = new StubChatClient((_, _) =>
        {
            var usage = new UsageDetails
            {
                InputTokenCount = 1000,
                OutputTokenCount = 50,
                AdditionalCounts = new AdditionalPropertiesDictionary<long>
                {
                    ["cached_input_tokens"] = 900
                }
            };
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "{}"))
            {
                Usage = usage
            };
        });

        var call = new LlmCall<JsonObject>(chat, MakeDescriptor(CachePolicy.Default));

        var result = await call.InvokeAsync("x");

        Assert.Equal(1000L, result.InputTokens);
        Assert.Equal(50L, result.OutputTokens);
        Assert.Equal(900L, result.CachedInputTokens);
        // Total = Input + Output when both surfaced (per ADR-0065).
        Assert.Equal(1050L, result.TotalTokens);
    }

    [Fact]
    public async Task CachedInputTokens_null_when_AdditionalCounts_absent()
    {
        var chat = new StubChatClient((_, _) =>
        {
            var usage = new UsageDetails
            {
                InputTokenCount = 100,
                OutputTokenCount = 10
                // No AdditionalCounts
            };
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "{}"))
            {
                Usage = usage
            };
        });

        var call = new LlmCall<JsonObject>(chat, MakeDescriptor(CachePolicy.Default));

        var result = await call.InvokeAsync("x");

        Assert.Equal(100L, result.InputTokens);
        Assert.Equal(10L, result.OutputTokens);
        Assert.Null(result.CachedInputTokens);
        Assert.Equal(110L, result.TotalTokens);
    }

    [Fact]
    public async Task CachedInputTokens_null_when_AdditionalCounts_key_unknown()
    {
        var chat = new StubChatClient((_, _) =>
        {
            var usage = new UsageDetails
            {
                InputTokenCount = 100,
                OutputTokenCount = 10,
                AdditionalCounts = new AdditionalPropertiesDictionary<long>
                {
                    ["some_unrelated_provider_metric"] = 999
                }
            };
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "{}"))
            {
                Usage = usage
            };
        });

        var call = new LlmCall<JsonObject>(chat, MakeDescriptor(CachePolicy.Default));

        var result = await call.InvokeAsync("x");

        Assert.Null(result.CachedInputTokens);
    }

    [Theory]
    [InlineData("cached_input_tokens")]
    [InlineData("InputTokenCount.Cached")]
    [InlineData("prompt_tokens_details.cached_tokens")]
    [InlineData("cache_read_input_tokens")]
    public async Task CachedInputTokens_recognises_known_provider_key(string keyName)
    {
        // ADR-0065: the mechanism scans AdditionalCounts for a few known
        // key conventions (Anthropic / OpenAI / OpenAI-via-MEAI variants).
        var chat = new StubChatClient((_, _) =>
        {
            var usage = new UsageDetails
            {
                InputTokenCount = 500,
                OutputTokenCount = 30,
                AdditionalCounts = new AdditionalPropertiesDictionary<long>
                {
                    [keyName] = 400
                }
            };
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "{}"))
            {
                Usage = usage
            };
        });

        var call = new LlmCall<JsonObject>(chat, MakeDescriptor(CachePolicy.Default));

        var result = await call.InvokeAsync("x");

        Assert.Equal(400L, result.CachedInputTokens);
    }

    [Fact]
    public async Task TotalTokens_falls_back_to_UsageDetails_TotalTokenCount_when_split_absent()
    {
        var chat = new StubChatClient((_, _) =>
        {
            var usage = new UsageDetails
            {
                // Provider surfaces only the total — no split.
                TotalTokenCount = 42,
            };
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "{}"))
            {
                Usage = usage
            };
        });

        var call = new LlmCall<JsonObject>(chat, MakeDescriptor(CachePolicy.Default));

        var result = await call.InvokeAsync("x");

        Assert.Null(result.InputTokens);
        Assert.Null(result.OutputTokens);
        Assert.Equal(42L, result.TotalTokens);
    }

    [Fact]
    public async Task All_usage_fields_null_when_Usage_absent_entirely()
    {
        var chat = new StubChatClient(_ => "{}");

        var call = new LlmCall<JsonObject>(chat, MakeDescriptor(CachePolicy.Default));

        var result = await call.InvokeAsync("x");

        Assert.Null(result.InputTokens);
        Assert.Null(result.OutputTokens);
        Assert.Null(result.CachedInputTokens);
        Assert.Null(result.TotalTokens);
    }

    [Fact]
    public async Task Split_usage_sums_independently_across_retry()
    {
        var calls = 0;
        var chat = new StubChatClient((_, _) =>
        {
            calls++;
            var usage = new UsageDetails
            {
                InputTokenCount = 100,
                OutputTokenCount = 10,
                AdditionalCounts = new AdditionalPropertiesDictionary<long>
                {
                    ["cached_input_tokens"] = 50
                }
            };
            var body = calls == 1 ? "not json" : "{}";
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, body))
            {
                Usage = usage
            };
        });

        var call = new LlmCall<JsonObject>(chat, MakeDescriptor(CachePolicy.Default));

        var result = await call.InvokeAsync("x");

        // Both calls' usage summed field-by-field — ADR-0065 endorsed
        // by M.E.AI's "all values set here are assumed to be summable"
        // contract on UsageDetails.AdditionalCounts.
        Assert.Equal(200L, result.InputTokens);
        Assert.Equal(20L, result.OutputTokens);
        Assert.Equal(100L, result.CachedInputTokens);
        Assert.Equal(220L, result.TotalTokens);
        Assert.Equal(1, result.ParseRetries);
    }

    [Fact]
    public async Task Usage_sum_respects_null_on_one_side()
    {
        // First call: full usage. Retry: no usage. The accumulator
        // should keep the first call's values.
        var calls = 0;
        var chat = new StubChatClient((_, _) =>
        {
            calls++;
            var body = calls == 1 ? "not json" : "{}";
            if (calls == 1)
            {
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, body))
                {
                    Usage = new UsageDetails
                    {
                        InputTokenCount = 100,
                        OutputTokenCount = 10,
                    }
                };
            }
            // Second call surfaces no Usage at all.
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, body));
        });

        var call = new LlmCall<JsonObject>(chat, MakeDescriptor(CachePolicy.Default));

        var result = await call.InvokeAsync("x");

        Assert.Equal(100L, result.InputTokens);
        Assert.Equal(10L, result.OutputTokens);
        Assert.Equal(110L, result.TotalTokens);
    }

    // ---- Helpers -------------------------------------------------------------

    private static LlmCallDescriptor<JsonObject> MakeDescriptor(CachePolicy cachePolicy)
        => new()
        {
            Name = "test",
            SystemPrompt = "s",
            BuildUserMessage = _ => "u",
            ParseResponse = _ => new JsonObject(),
            SystemPromptCache = cachePolicy,
        };

    // Local copy of the StubChatClient from LlmCallTests — kept private
    // to avoid coupling the two test files. Two overloads cover the
    // patterns this file needs.
    private sealed class StubChatClient : IChatClient
    {
        private readonly Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, ChatResponse> _respond;

        public StubChatClient(Func<IEnumerable<ChatMessage>, string> respond)
            : this((m, _, _) => StringResp(respond(m))) { }

        public StubChatClient(Func<IEnumerable<ChatMessage>, ChatOptions?, string> respond)
            : this((m, o, _) => StringResp(respond(m, o))) { }

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

    private sealed class NoopAIFunction : AIFunction
    {
        public override string Name { get; }
        public override string Description => "noop";

        public NoopAIFunction(string name) { Name = name; }

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments, CancellationToken cancellationToken)
            => ValueTask.FromResult<object?>(null);
    }
}
