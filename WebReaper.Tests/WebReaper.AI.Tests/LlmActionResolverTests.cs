using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using WebReaper.AI;
using WebReaper.Domain.PageActions;

namespace WebReaper.AI.Tests;

// ADR-0050: the LLM-backed IActionResolver. Tests use a stub IChatClient
// that returns a canned response — no real model call — so we can pin the
// prompt composition, JSON parsing, every supported arm shape, the unknown
// shape -> null contract, code-fence stripping, and the never-return-
// SemanticAct discipline (a SemanticAct response is treated as unknown
// because the prompt's whitelist doesn't include it).
public class LlmActionResolverTests
{
    [Fact]
    public async Task Returns_Click_for_click_intent_and_valid_json()
    {
        var chat = new StubChatClient(_ =>
            "{\"kind\":\"click\",\"selector\":\".header-nav .signin\"}");

        var result = await new LlmActionResolver(chat).ResolveAsync(
            "click sign in", "<html><body><button class=\"signin\"/></body></html>");

        var click = Assert.IsType<PageAction.Click>(result);
        Assert.Equal(".header-nav .signin", click.Selector);
    }

    [Fact]
    public async Task Returns_WaitForSelector_for_waitFor_intent_with_timeout()
    {
        var chat = new StubChatClient(_ =>
            "{\"kind\":\"waitFor\",\"selector\":\".modal\",\"timeoutMs\":2500}");

        var result = await new LlmActionResolver(chat).ResolveAsync(
            "wait for the modal", "<html/>");

        var wfs = Assert.IsType<PageAction.WaitForSelector>(result);
        Assert.Equal(".modal", wfs.Selector);
        Assert.Equal(2500, wfs.TimeoutMs);
    }

    [Fact]
    public async Task WaitFor_without_timeoutMs_defaults_to_30s()
    {
        var chat = new StubChatClient(_ =>
            "{\"kind\":\"waitFor\",\"selector\":\".modal\"}");

        var result = await new LlmActionResolver(chat).ResolveAsync("wait", "<html/>");

        var wfs = Assert.IsType<PageAction.WaitForSelector>(result);
        Assert.Equal(30_000, wfs.TimeoutMs);
    }

    [Fact]
    public async Task Returns_Wait_for_wait_intent_with_ms()
    {
        var chat = new StubChatClient(_ => "{\"kind\":\"wait\",\"ms\":500}");

        var result = await new LlmActionResolver(chat).ResolveAsync("pause", "<html/>");

        var wait = Assert.IsType<PageAction.Wait>(result);
        Assert.Equal(500, wait.Milliseconds);
    }

    [Fact]
    public async Task Returns_EvaluateExpression_for_evaluate_intent()
    {
        var chat = new StubChatClient(_ =>
            "{\"kind\":\"evaluate\",\"expression\":\"document.title\"}");

        var result = await new LlmActionResolver(chat).ResolveAsync("run js", "<html/>");

        var eval = Assert.IsType<PageAction.EvaluateExpression>(result);
        Assert.Equal("document.title", eval.Expression);
    }

    [Fact]
    public async Task Returns_null_for_unknown_kind()
    {
        var chat = new StubChatClient(_ => "{\"kind\":\"submit\",\"selector\":\".form\"}");

        var result = await new LlmActionResolver(chat).ResolveAsync("submit", "<html/>");

        Assert.Null(result);
    }

    [Fact]
    public async Task Returns_null_for_click_without_selector()
    {
        var chat = new StubChatClient(_ => "{\"kind\":\"click\"}");

        var result = await new LlmActionResolver(chat).ResolveAsync("click", "<html/>");

        Assert.Null(result);
    }

    [Fact]
    public async Task Returns_null_for_empty_object()
    {
        var chat = new StubChatClient(_ => "{}");

        var result = await new LlmActionResolver(chat).ResolveAsync("anything", "<html/>");

        Assert.Null(result);
    }

    [Fact]
    public async Task Returns_null_for_malformed_json()
    {
        var chat = new StubChatClient(_ => "not json at all");

        var result = await new LlmActionResolver(chat).ResolveAsync("anything", "<html/>");

        Assert.Null(result);
    }

    [Fact]
    public async Task Resolver_never_returns_SemanticAct_unknown_kind_treats_as_null()
    {
        // The prompt's whitelist doesn't include "semanticAct" so even if the
        // model tries to return one, ParseArm doesn't know it -> null. This
        // closes the loop the SemanticActCoordinator also guards against.
        var chat = new StubChatClient(_ =>
            "{\"kind\":\"semanticAct\",\"intent\":\"do something else\"}");

        var result = await new LlmActionResolver(chat).ResolveAsync("outer", "<html/>");

        Assert.Null(result);
    }

    [Fact]
    public async Task Retries_once_on_invalid_JSON_then_succeeds()
    {
        // ADR-0059 regression: the LlmCall mechanism's bounded parse-retry
        // recovers when the first response is malformed JSON.
        var calls = 0;
        var chat = new StubChatClient(_ =>
        {
            calls++;
            return calls == 1
                ? "{\"kind\":\"click\","          // trailing comma -> JsonException
                : "{\"kind\":\"click\",\"selector\":\".retried\"}";
        });

        var result = await new LlmActionResolver(chat).ResolveAsync("click", "<html/>");

        Assert.Equal(2, calls);
        Assert.Equal(".retried", Assert.IsType<PageAction.Click>(result).Selector);
    }

    [Fact]
    public async Task Strips_markdown_code_fences_with_language_tag()
    {
        var chat = new StubChatClient(_ =>
            "```json\n{\"kind\":\"click\",\"selector\":\".x\"}\n```");

        var result = await new LlmActionResolver(chat).ResolveAsync("click", "<html/>");

        Assert.Equal(".x", Assert.IsType<PageAction.Click>(result).Selector);
    }

    [Fact]
    public async Task Strips_bare_triple_fences_without_language_tag()
    {
        var chat = new StubChatClient(_ =>
            "```\n{\"kind\":\"click\",\"selector\":\".x\"}\n```");

        var result = await new LlmActionResolver(chat).ResolveAsync("click", "<html/>");

        Assert.Equal(".x", Assert.IsType<PageAction.Click>(result).Selector);
    }

    [Fact]
    public async Task Prompt_contains_intent_and_page_html()
    {
        List<ChatMessage>? capturedMessages = null;
        var chat = new StubChatClient((msgs, _) =>
        {
            capturedMessages = msgs.ToList();
            return "{\"kind\":\"click\",\"selector\":\".x\"}";
        });

        await new LlmActionResolver(chat).ResolveAsync(
            "click sign in", "<html><body>SIGNIN BUTTON</body></html>");

        Assert.NotNull(capturedMessages);
        var userMsg = capturedMessages!.Single(m => m.Role == ChatRole.User).Text;
        Assert.Contains("click sign in", userMsg);
        Assert.Contains("SIGNIN BUTTON", userMsg);
    }

    [Fact]
    public async Task System_prompt_whitelists_the_four_shapes()
    {
        List<ChatMessage>? capturedMessages = null;
        var chat = new StubChatClient((msgs, _) =>
        {
            capturedMessages = msgs.ToList();
            return "{}";
        });

        await new LlmActionResolver(chat).ResolveAsync("anything", "<html/>");

        var systemMsg = capturedMessages!.Single(m => m.Role == ChatRole.System).Text;
        Assert.Contains("click", systemMsg);
        Assert.Contains("waitFor", systemMsg);
        Assert.Contains("wait", systemMsg);
        Assert.Contains("evaluate", systemMsg);
        // The whitelist deliberately does NOT include semanticAct.
        Assert.DoesNotContain("semanticAct", systemMsg);
    }

    [Fact]
    public async Task Truncates_html_at_MaxHtmlChars()
    {
        var big = new string('x', 50_000);
        string? capturedUser = null;
        var chat = new StubChatClient((msgs, _) =>
        {
            capturedUser = msgs.Single(m => m.Role == ChatRole.User).Text;
            return "{\"kind\":\"click\",\"selector\":\".x\"}";
        });

        await new LlmActionResolver(chat, new LlmActionResolverOptions(MaxHtmlChars: 1000))
            .ResolveAsync("intent", big);

        Assert.NotNull(capturedUser);
        // user prompt has the intent line + the truncated HTML; cap on the
        // raw HTML, not on the whole prompt — assert the trim happened.
        Assert.True(capturedUser!.Length < 50_000, "expected HTML to be truncated below 50000 chars");
    }

    [Fact]
    public async Task ChatOptions_carries_model_temperature_and_max_response_tokens()
    {
        ChatOptions? capturedOpts = null;
        var chat = new StubChatClient((_, opts) =>
        {
            capturedOpts = opts;
            return "{\"kind\":\"wait\",\"ms\":0}";
        });

        await new LlmActionResolver(chat, new LlmActionResolverOptions(
            Model: "claude-sonnet-4-6", Temperature: 0.3f, MaxResponseTokens: 256))
            .ResolveAsync("intent", "<html/>");

        Assert.NotNull(capturedOpts);
        Assert.Equal("claude-sonnet-4-6", capturedOpts!.ModelId);
        Assert.Equal(0.3f, capturedOpts.Temperature);
        Assert.Equal(256, capturedOpts.MaxOutputTokens);
        Assert.NotNull(capturedOpts.ResponseFormat);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveAsync_rejects_blank_intent(string? intent)
    {
        var chat = new StubChatClient(_ => "{}");
        // ArgumentException family — ArgumentNullException for null,
        // ArgumentException for empty/whitespace. ThrowsAnyAsync pins the
        // family without coupling to the exact subtype.
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            new LlmActionResolver(chat).ResolveAsync(intent!, "<html/>"));
    }

    [Fact]
    public void Constructor_rejects_null_chat_client()
    {
        Assert.Throws<ArgumentNullException>(() => new LlmActionResolver(null!));
    }

    // Stub IChatClient — same shape as LlmContentExtractorTests' stub, kept
    // local to this test for symmetry; extracting to a shared file would
    // muddy the tier-1 / tier-2 split (each test class owns its own seam).
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
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        }

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
