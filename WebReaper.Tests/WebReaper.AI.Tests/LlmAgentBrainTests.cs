using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using WebReaper.AI;
using WebReaper.Domain.Agent;
using WebReaper.Domain.PageActions;

namespace WebReaper.AI.Tests;

// ADR-0051: the LLM-backed IAgentBrain. Tests use a stub IChatClient that
// returns a canned response so we pin the prompt composition, decision
// parsing, every arm shape, malformed-JSON-to-Stop discipline, and
// code-fence stripping (mirror of LlmActionResolverTests).
public class LlmAgentBrainTests
{
    private static readonly AgentState DummyState = new(
        Goal: "get all products",
        CurrentUrl: "https://shop.example.com/",
        CurrentPageMarkdown: "# Welcome",
        CandidateUrls: new[] { "https://shop.example.com/products" },
        Extracted: Array.Empty<JsonObject>(),
        History: Array.Empty<AgentDecision>(),
        VisitedUrls: new[] { "https://shop.example.com/" },
        StepNumber: 0);

    [Fact]
    public async Task Returns_Stop_for_stop_type_with_reason()
    {
        var chat = new StubChatClient(_ => "{\"type\":\"stop\",\"reason\":\"goal satisfied\"}");
        var brain = new LlmAgentBrain(chat);

        var decision = await brain.DecideAsync(DummyState);

        var stop = Assert.IsType<AgentDecision.Stop>(decision);
        Assert.Equal("goal satisfied", stop.Reason);
    }

    [Fact]
    public async Task Returns_Follow_for_follow_type_with_url()
    {
        var chat = new StubChatClient(_ =>
            "{\"type\":\"follow\",\"reason\":\"product listings here\",\"url\":\"https://shop.example.com/products\"}");
        var brain = new LlmAgentBrain(chat);

        var decision = await brain.DecideAsync(DummyState);

        var follow = Assert.IsType<AgentDecision.Follow>(decision);
        Assert.Equal("https://shop.example.com/products", follow.Url);
        Assert.Equal("product listings here", follow.Reason);
    }

    [Fact]
    public async Task Returns_Extract_for_extract_type_with_flat_schema()
    {
        var chat = new StubChatClient(_ =>
            "{\"type\":\"extract\",\"reason\":\"page is a product\",\"schema\":{\"title\":\"h1\",\"price\":\".price\"}}");
        var brain = new LlmAgentBrain(chat);

        var decision = await brain.DecideAsync(DummyState);

        var extract = Assert.IsType<AgentDecision.Extract>(decision);
        Assert.Equal(2, extract.Schema.Children.Count);
        Assert.Contains(extract.Schema.Children, e => e.Field == "title" && e.Selector == "h1");
        Assert.Contains(extract.Schema.Children, e => e.Field == "price" && e.Selector == ".price");
    }

    [Fact]
    public async Task Returns_Act_for_act_type_with_click_action()
    {
        var chat = new StubChatClient(_ =>
            "{\"type\":\"act\",\"reason\":\"sign in first\",\"action\":{\"kind\":\"click\",\"selector\":\".signin\"}}");
        var brain = new LlmAgentBrain(chat);

        var decision = await brain.DecideAsync(DummyState);

        var act = Assert.IsType<AgentDecision.Act>(decision);
        var click = Assert.IsType<PageAction.Click>(act.Action);
        Assert.Equal(".signin", click.Selector);
    }

    [Fact]
    public async Task Returns_Stop_for_malformed_JSON()
    {
        var chat = new StubChatClient(_ => "this is not json");
        var brain = new LlmAgentBrain(chat);

        var decision = await brain.DecideAsync(DummyState);

        Assert.IsType<AgentDecision.Stop>(decision);
    }

    [Fact]
    public async Task Returns_Stop_for_unknown_type()
    {
        var chat = new StubChatClient(_ => "{\"type\":\"explode\",\"reason\":\"oops\"}");
        var brain = new LlmAgentBrain(chat);

        var decision = await brain.DecideAsync(DummyState);

        Assert.IsType<AgentDecision.Stop>(decision);
    }

    [Fact]
    public async Task Returns_Stop_when_extract_schema_is_empty()
    {
        var chat = new StubChatClient(_ =>
            "{\"type\":\"extract\",\"reason\":\"empty\",\"schema\":{}}");
        var brain = new LlmAgentBrain(chat);

        var decision = await brain.DecideAsync(DummyState);

        Assert.IsType<AgentDecision.Stop>(decision);
    }

    [Fact]
    public async Task Returns_Stop_when_follow_url_is_missing()
    {
        var chat = new StubChatClient(_ =>
            "{\"type\":\"follow\",\"reason\":\"no url\"}");
        var brain = new LlmAgentBrain(chat);

        var decision = await brain.DecideAsync(DummyState);

        Assert.IsType<AgentDecision.Stop>(decision);
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
                ? "{\"type\":\"stop\","   // trailing comma -> JsonException
                : "{\"type\":\"stop\",\"reason\":\"after retry\"}";
        });
        var brain = new LlmAgentBrain(chat);

        var decision = await brain.DecideAsync(DummyState);

        Assert.Equal(2, calls);
        var stop = Assert.IsType<AgentDecision.Stop>(decision);
        Assert.Equal("after retry", stop.Reason);
    }

    [Fact]
    public async Task Strips_Markdown_code_fences_around_JSON()
    {
        var chat = new StubChatClient(_ =>
            "```json\n{\"type\":\"stop\",\"reason\":\"fenced\"}\n```");
        var brain = new LlmAgentBrain(chat);

        var decision = await brain.DecideAsync(DummyState);

        var stop = Assert.IsType<AgentDecision.Stop>(decision);
        Assert.Equal("fenced", stop.Reason);
    }

    [Fact]
    public async Task Prompt_includes_goal_currentUrl_candidates_and_visited()
    {
        string? capturedUserPrompt = null;
        var chat = new StubChatClient(msgs =>
        {
            capturedUserPrompt = msgs.Last(m => m.Role == ChatRole.User).Text;
            return "{\"type\":\"stop\",\"reason\":\"ok\"}";
        });
        var brain = new LlmAgentBrain(chat);

        await brain.DecideAsync(DummyState);

        Assert.NotNull(capturedUserPrompt);
        Assert.Contains("get all products", capturedUserPrompt);
        Assert.Contains("https://shop.example.com/", capturedUserPrompt);
        Assert.Contains("https://shop.example.com/products", capturedUserPrompt);
        Assert.Contains("# Welcome", capturedUserPrompt);
    }

    [Fact]
    public async Task LlmAgent_RunAsync_end_to_end_via_satellite_sugar()
    {
        // Pins the satellite sugar surface — three-arg one-liner constructs
        // LlmAgentBrain internally and runs the engine.
        var chat = new StubChatClient(_ => "{\"type\":\"stop\",\"reason\":\"trivial\"}");

        var result = await LlmAgent.RunAsync(
            "https://example.com/", "test", chat,
            configure: b => b.WithPageLoader(new FakePageLoader("<html/>")));

        Assert.Equal("trivial", result.TerminationReason);
        Assert.Equal(1, result.StepsExecuted);
    }

    private sealed class StubChatClient : IChatClient
    {
        private readonly Func<IEnumerable<ChatMessage>, string> _respond;

        public StubChatClient(Func<IEnumerable<ChatMessage>, string> respond)
            => _respond = respond;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _respond(messages))));

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

    private sealed class FakePageLoader : WebReaper.Core.Loaders.Abstract.IPageLoader
    {
        private readonly string _html;
        public FakePageLoader(string html) => _html = html;
        public Task<string> LoadAsync(
            WebReaper.Core.Loaders.Abstract.PageRequest request,
            CancellationToken ct = default)
            => Task.FromResult(_html);
    }
}
