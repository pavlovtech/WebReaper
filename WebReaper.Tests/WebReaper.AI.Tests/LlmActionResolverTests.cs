using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using WebReaper.AI;
using WebReaper.Domain.PageActions;

namespace WebReaper.AI.Tests;

// ADR-0050 + ADR-0060: the LLM-backed IActionResolver, post tool-calling
// pivot. Tests use a stub IChatClient that returns canned FunctionCallContent
// — no real model call — so we can pin tool-call composition, every supported
// arm shape, the unknown-tool-name -> null contract, and the never-return-
// SemanticAct discipline (structural — the resolver's tool registry never
// includes ActSemanticAct, fork 8).
public class LlmActionResolverTests
{
    // ---- Concrete arms ------------------------------------------------------

    [Fact]
    public async Task Returns_Click_for_ActClick_tool_call()
    {
        var chat = ToolCallStub("ActClick",
            ("selector", ".header-nav .signin"));

        var result = await new LlmActionResolver(chat).ResolveAsync(
            "click sign in", "<html><body><button class=\"signin\"/></body></html>");

        var click = Assert.IsType<PageAction.Click>(result);
        Assert.Equal(".header-nav .signin", click.Selector);
    }

    [Fact]
    public async Task Returns_WaitForSelector_for_ActWaitForSelector_with_timeout()
    {
        var chat = ToolCallStub("ActWaitForSelector",
            ("selector", ".modal"), ("timeoutMs", 2500));

        var result = await new LlmActionResolver(chat).ResolveAsync(
            "wait for the modal", "<html/>");

        var wfs = Assert.IsType<PageAction.WaitForSelector>(result);
        Assert.Equal(".modal", wfs.Selector);
        Assert.Equal(2500, wfs.TimeoutMs);
    }

    [Fact]
    public async Task ActWaitForSelector_without_timeoutMs_defaults_to_30s()
    {
        var chat = ToolCallStub("ActWaitForSelector", ("selector", ".modal"));

        var result = await new LlmActionResolver(chat).ResolveAsync("wait", "<html/>");

        var wfs = Assert.IsType<PageAction.WaitForSelector>(result);
        Assert.Equal(30_000, wfs.TimeoutMs);
    }

    [Fact]
    public async Task Returns_Wait_for_ActWait_with_ms()
    {
        var chat = ToolCallStub("ActWait", ("ms", 500));

        var result = await new LlmActionResolver(chat).ResolveAsync("pause", "<html/>");

        var wait = Assert.IsType<PageAction.Wait>(result);
        Assert.Equal(500, wait.Milliseconds);
    }

    [Fact]
    public async Task Returns_EvaluateExpression_for_ActEvaluate()
    {
        var chat = ToolCallStub("ActEvaluate", ("expression", "document.title"));

        var result = await new LlmActionResolver(chat).ResolveAsync("run js", "<html/>");

        var eval = Assert.IsType<PageAction.EvaluateExpression>(result);
        Assert.Equal("document.title", eval.Expression);
    }

    [Fact]
    public async Task Returns_WaitForNetworkIdle_for_parameterless_tool()
    {
        var chat = ToolCallStub("ActWaitForNetworkIdle");

        var result = await new LlmActionResolver(chat).ResolveAsync("settle", "<html/>");

        Assert.IsType<PageAction.WaitForNetworkIdle>(result);
    }

    [Fact]
    public async Task Returns_ScrollToEnd_for_parameterless_tool()
    {
        var chat = ToolCallStub("ActScrollToEnd");

        var result = await new LlmActionResolver(chat).ResolveAsync("scroll", "<html/>");

        Assert.IsType<PageAction.ScrollToEnd>(result);
    }

    // ---- Unknown / malformed -----------------------------------------------

    [Fact]
    public async Task Returns_null_for_unknown_tool_name()
    {
        // Model called a function with a name that's not in the registry.
        // (E.g. provider hallucination or a brain-only tool — the closed
        // sum's structural protection.)
        var chat = ToolCallStub("ActSubmit", ("selector", ".form"));

        var result = await new LlmActionResolver(chat).ResolveAsync("submit", "<html/>");

        Assert.Null(result);
    }

    [Fact]
    public async Task Returns_null_for_ActClick_without_selector()
    {
        // Required arg missing — closed-sum-at-the-LLM-boundary still has a
        // belt-and-braces guard in ParseToolCall.
        var chat = ToolCallStub("ActClick");

        var result = await new LlmActionResolver(chat).ResolveAsync("click", "<html/>");

        Assert.Null(result);
    }

    [Fact]
    public async Task Returns_null_when_model_omits_tool_call()
    {
        // Two strikes (initial + retry) of plain-text response -> the
        // mechanism throws LlmCallException -> adapter swallows -> null.
        var chat = new StubChatClient(_ => "I cannot do that.");

        var result = await new LlmActionResolver(chat).ResolveAsync("anything", "<html/>");

        Assert.Null(result);
    }

    [Fact]
    public async Task Resolver_tool_registry_excludes_ActSemanticAct_so_resolver_cannot_loop()
    {
        // Fork 8: the resolver's tool registry never contains
        // ActSemanticAct — the model literally cannot call it. If the
        // provider hallucinates one, the adapter treats it as unknown.
        var chat = ToolCallStub("ActSemanticAct", ("intent", "do something else"));

        var result = await new LlmActionResolver(chat).ResolveAsync("outer", "<html/>");

        Assert.Null(result);
    }

    // ---- On-the-wire request shape -----------------------------------------
    //
    // The most useful "before-and-after" regression: pin that the
    // ChatOptions.Tools list is set (six entries, the right names) and the
    // ResponseFormat is NOT — providers reject combining the two.

    [Fact]
    public async Task ChatOptions_carries_the_nine_resolver_tools_and_no_ResponseFormat()
    {
        ChatOptions? captured = null;
        var chat = new StubChatClient((_, opts) =>
        {
            captured = opts;
            return ToolCallResponse("ActClick", ("selector", ".x"));
        });

        await new LlmActionResolver(chat).ResolveAsync("click", "<html/>");

        Assert.NotNull(captured);
        Assert.NotNull(captured!.Tools);
        Assert.Equal(9, captured.Tools!.Count);
        var names = captured.Tools.OfType<AIFunction>().Select(f => f.Name).ToHashSet();
        Assert.Contains("ActClick", names);
        Assert.Contains("ActWait", names);
        Assert.Contains("ActWaitForSelector", names);
        Assert.Contains("ActWaitForNetworkIdle", names);
        Assert.Contains("ActScrollToEnd", names);
        Assert.Contains("ActEvaluate", names);
        // ADR-0074 arms
        Assert.Contains("ActScrollIntoView", names);
        Assert.Contains("ActPress", names);
        Assert.Contains("ActFill", names);
        // Structural — fork 8.
        Assert.DoesNotContain("ActSemanticAct", names);
        // Tool-call mode does NOT set ResponseFormat (providers reject the combo).
        Assert.Null(captured.ResponseFormat);
    }

    [Fact]
    public async Task System_prompt_no_longer_enumerates_JSON_shapes()
    {
        // Post-pivot the system prompt is short — the tool list IS the
        // schema. The old "kind: click/waitFor/..." JSON enumeration is
        // gone. This is a behavioural pin so a regression to a JSON-mode
        // shape is impossible without test failure.
        List<ChatMessage>? captured = null;
        var chat = new StubChatClient((msgs, _) =>
        {
            captured = msgs.ToList();
            return ToolCallResponse("ActClick", ("selector", ".x"));
        });

        await new LlmActionResolver(chat).ResolveAsync("intent", "<html/>");

        var systemMsg = captured!.Single(m => m.Role == ChatRole.System).Text;
        // The prompt mentions calling tools, not returning JSON shapes.
        Assert.Contains("tool", systemMsg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"kind\"", systemMsg);
    }

    [Fact]
    public async Task Prompt_still_contains_intent_and_page_html()
    {
        List<ChatMessage>? captured = null;
        var chat = new StubChatClient((msgs, _) =>
        {
            captured = msgs.ToList();
            return ToolCallResponse("ActClick", ("selector", ".x"));
        });

        await new LlmActionResolver(chat).ResolveAsync(
            "click sign in", "<html><body>SIGNIN BUTTON</body></html>");

        var userMsg = captured!.Single(m => m.Role == ChatRole.User).Text;
        Assert.Contains("click sign in", userMsg);
        Assert.Contains("SIGNIN BUTTON", userMsg);
    }

    [Fact]
    public async Task Truncates_html_at_MaxHtmlChars()
    {
        var big = new string('x', 50_000);
        string? capturedUser = null;
        var chat = new StubChatClient((msgs, _) =>
        {
            capturedUser = msgs.Single(m => m.Role == ChatRole.User).Text;
            return ToolCallResponse("ActClick", ("selector", ".x"));
        });

        await new LlmActionResolver(chat, new LlmActionResolverOptions(MaxHtmlChars: 1000))
            .ResolveAsync("intent", big);

        Assert.True(capturedUser!.Length < 50_000, "expected HTML to be truncated below 50000 chars");
    }

    [Fact]
    public async Task ChatOptions_carries_model_temperature_and_max_response_tokens()
    {
        ChatOptions? captured = null;
        var chat = new StubChatClient((_, opts) =>
        {
            captured = opts;
            return ToolCallResponse("ActWait", ("ms", 0));
        });

        await new LlmActionResolver(chat, new LlmActionResolverOptions(
            Model: "claude-sonnet-4-6", Temperature: 0.3f, MaxResponseTokens: 256))
            .ResolveAsync("intent", "<html/>");

        Assert.NotNull(captured);
        Assert.Equal("claude-sonnet-4-6", captured!.ModelId);
        Assert.Equal(0.3f, captured.Temperature);
        Assert.Equal(256, captured.MaxOutputTokens);
        // Tool-call mode: no ResponseFormat.
        Assert.Null(captured.ResponseFormat);
    }

    // ---- Argument validation -----------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveAsync_rejects_blank_intent(string? intent)
    {
        var chat = ToolCallStub("ActClick", ("selector", ".x"));
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            new LlmActionResolver(chat).ResolveAsync(intent!, "<html/>"));
    }

    [Fact]
    public void Constructor_rejects_null_chat_client()
    {
        Assert.Throws<ArgumentNullException>(() => new LlmActionResolver(null!));
    }

    // ---- Helpers -----------------------------------------------------------

    private static StubChatClient ToolCallStub(string name, params (string Name, object Value)[] args)
        => new(_ => ToolCallResponse(name, args));

    private static ChatResponse ToolCallResponse(string name, params (string Name, object Value)[] args)
    {
        var dict = new Dictionary<string, object?>(args.Length);
        foreach (var (k, v) in args) dict[k] = v;
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent>
        {
            new FunctionCallContent("call-1", name, dict)
        }));
    }

    private sealed class StubChatClient : IChatClient
    {
        private readonly Func<IEnumerable<ChatMessage>, ChatOptions?, ChatResponse> _respond;

        public StubChatClient(Func<IEnumerable<ChatMessage>, string> respond)
            : this((m, _) => new ChatResponse(new ChatMessage(ChatRole.Assistant, respond(m)))) { }

        public StubChatClient(Func<IEnumerable<ChatMessage>, ChatOptions?, string> respond)
            : this((m, o) => new ChatResponse(new ChatMessage(ChatRole.Assistant, respond(m, o)))) { }

        public StubChatClient(Func<IEnumerable<ChatMessage>, ChatResponse> respond)
            : this((m, _) => respond(m)) { }

        public StubChatClient(Func<IEnumerable<ChatMessage>, ChatOptions?, ChatResponse> respond)
            => _respond = respond;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_respond(messages, options));

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
