using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using WebReaper.AI;
using WebReaper.Domain.Agent;
using WebReaper.Domain.PageActions;

namespace WebReaper.AI.Tests;

// ADR-0051 + ADR-0060: the LLM-backed IAgentBrain, post tool-calling pivot.
// Tests use a stub IChatClient that returns canned FunctionCallContent for
// each registered tool name; the brain translates to the AgentDecision arm.
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

    // ---- Decision arm dispatch ---------------------------------------------

    [Fact]
    public async Task Returns_Stop_for_Stop_tool_call()
    {
        var chat = ToolCallStub("Stop", ("reason", "goal satisfied"));
        var brain = new LlmAgentBrain(chat);

        var decision = await brain.DecideAsync(DummyState);

        var stop = Assert.IsType<AgentDecision.Stop>(decision);
        Assert.Equal("goal satisfied", stop.Reason);
    }

    [Fact]
    public async Task Returns_Follow_for_Follow_tool_call()
    {
        var chat = ToolCallStub("Follow",
            ("url", "https://shop.example.com/products"),
            ("reason", "product listings here"));
        var brain = new LlmAgentBrain(chat);

        var decision = await brain.DecideAsync(DummyState);

        var follow = Assert.IsType<AgentDecision.Follow>(decision);
        Assert.Equal("https://shop.example.com/products", follow.Url);
        Assert.Equal("product listings here", follow.Reason);
    }

    [Fact]
    public async Task Returns_Extract_for_Extract_tool_call_with_flat_schema()
    {
        var schema = new Dictionary<string, object?> { ["title"] = "h1", ["price"] = ".price" };
        var chat = ToolCallStub("Extract",
            ("schema", schema), ("reason", "page is a product"));
        var brain = new LlmAgentBrain(chat);

        var decision = await brain.DecideAsync(DummyState);

        var extract = Assert.IsType<AgentDecision.Extract>(decision);
        Assert.Equal(2, extract.Schema.Children.Count);
        Assert.Contains(extract.Schema.Children, e => e.Field == "title" && e.Selector == "h1");
        Assert.Contains(extract.Schema.Children, e => e.Field == "price" && e.Selector == ".price");
        Assert.Equal("page is a product", extract.Reason);
    }

    [Fact]
    public async Task Returns_Act_Click_for_ActClick_tool_call()
    {
        var chat = ToolCallStub("ActClick",
            ("selector", ".signin"), ("reason", "sign in first"));
        var brain = new LlmAgentBrain(chat);

        var decision = await brain.DecideAsync(DummyState);

        var act = Assert.IsType<AgentDecision.Act>(decision);
        var click = Assert.IsType<PageAction.Click>(act.Action);
        Assert.Equal(".signin", click.Selector);
        Assert.Equal("sign in first", act.Reason);
    }

    [Fact]
    public async Task Returns_Act_Wait_for_ActWait_tool_call()
    {
        var chat = ToolCallStub("ActWait", ("ms", 500), ("reason", "let it settle"));
        var brain = new LlmAgentBrain(chat);

        var decision = await brain.DecideAsync(DummyState);

        var act = Assert.IsType<AgentDecision.Act>(decision);
        var wait = Assert.IsType<PageAction.Wait>(act.Action);
        Assert.Equal(500, wait.Milliseconds);
    }

    [Fact]
    public async Task Returns_Act_WaitForSelector_for_ActWaitForSelector_tool_call()
    {
        var chat = ToolCallStub("ActWaitForSelector",
            ("selector", ".modal"), ("timeoutMs", 5000),
            ("reason", "wait for modal"));
        var brain = new LlmAgentBrain(chat);

        var decision = await brain.DecideAsync(DummyState);

        var act = Assert.IsType<AgentDecision.Act>(decision);
        var wfs = Assert.IsType<PageAction.WaitForSelector>(act.Action);
        Assert.Equal(".modal", wfs.Selector);
        Assert.Equal(5000, wfs.TimeoutMs);
    }

    [Fact]
    public async Task Returns_Act_WaitForNetworkIdle_for_parameterless_tool()
    {
        var chat = ToolCallStub("ActWaitForNetworkIdle", ("reason", "settle"));
        var brain = new LlmAgentBrain(chat);

        var decision = await brain.DecideAsync(DummyState);

        var act = Assert.IsType<AgentDecision.Act>(decision);
        Assert.IsType<PageAction.WaitForNetworkIdle>(act.Action);
    }

    [Fact]
    public async Task Returns_Act_ScrollToEnd_for_parameterless_tool()
    {
        var chat = ToolCallStub("ActScrollToEnd", ("reason", "reveal more"));
        var brain = new LlmAgentBrain(chat);

        var decision = await brain.DecideAsync(DummyState);

        var act = Assert.IsType<AgentDecision.Act>(decision);
        Assert.IsType<PageAction.ScrollToEnd>(act.Action);
    }

    [Fact]
    public async Task Returns_Act_Evaluate_for_ActEvaluate_tool_call()
    {
        var chat = ToolCallStub("ActEvaluate",
            ("expression", "document.title"), ("reason", "inspect"));
        var brain = new LlmAgentBrain(chat);

        var decision = await brain.DecideAsync(DummyState);

        var act = Assert.IsType<AgentDecision.Act>(decision);
        var eval = Assert.IsType<PageAction.EvaluateExpression>(act.Action);
        Assert.Equal("document.title", eval.Expression);
    }

    [Fact]
    public async Task Returns_Act_SemanticAct_for_ActSemanticAct_tool_call()
    {
        // The brain CAN hand the resolver a semantic intent — that's the
        // brain's tool list; the resolver itself cannot loop.
        var chat = ToolCallStub("ActSemanticAct",
            ("intent", "open the menu"), ("reason", "let's see the nav"));
        var brain = new LlmAgentBrain(chat);

        var decision = await brain.DecideAsync(DummyState);

        var act = Assert.IsType<AgentDecision.Act>(decision);
        var sem = Assert.IsType<PageAction.SemanticAct>(act.Action);
        Assert.Equal("open the menu", sem.Intent);
    }

    // ---- Failure paths -----------------------------------------------------

    [Fact]
    public async Task Returns_Stop_with_structural_reason_when_model_omits_tool_call()
    {
        // Fork 5: a brain that returned no tool call (after the mechanism's
        // one-shot retry) defaults to Stop("brain returned no tool call ...");
        // engine logs and terminates.
        var chat = new StubChatClient(_ => "I refused to call any tool.");
        var brain = new LlmAgentBrain(chat);

        var decision = await brain.DecideAsync(DummyState);

        var stop = Assert.IsType<AgentDecision.Stop>(decision);
        Assert.Contains("brain returned no tool call", stop.Reason);
    }

    [Fact]
    public async Task Returns_Stop_for_unregistered_tool_name()
    {
        var chat = ToolCallStub("Explode", ("reason", "oops"));
        var brain = new LlmAgentBrain(chat);

        var decision = await brain.DecideAsync(DummyState);

        var stop = Assert.IsType<AgentDecision.Stop>(decision);
        Assert.Contains("unregistered tool 'Explode'", stop.Reason);
    }

    [Fact]
    public async Task Returns_Stop_when_Extract_schema_is_empty()
    {
        var chat = ToolCallStub("Extract",
            ("schema", new Dictionary<string, object?>()), ("reason", "empty"));
        var brain = new LlmAgentBrain(chat);

        var decision = await brain.DecideAsync(DummyState);

        var stop = Assert.IsType<AgentDecision.Stop>(decision);
        Assert.Contains("Extract schema was empty", stop.Reason);
    }

    [Fact]
    public async Task Returns_Stop_when_Follow_url_is_missing()
    {
        // Argument validation fallback even though the schema marks url required.
        var chat = ToolCallStub("Follow", ("reason", "no url"));
        var brain = new LlmAgentBrain(chat);

        var decision = await brain.DecideAsync(DummyState);

        var stop = Assert.IsType<AgentDecision.Stop>(decision);
        Assert.Contains("Follow missing 'url'", stop.Reason);
    }

    [Fact]
    public async Task Returns_Stop_when_ActClick_selector_is_missing()
    {
        var chat = ToolCallStub("ActClick", ("reason", "broken"));
        var brain = new LlmAgentBrain(chat);

        var decision = await brain.DecideAsync(DummyState);

        var stop = Assert.IsType<AgentDecision.Stop>(decision);
        Assert.Contains("ActClick missing 'selector'", stop.Reason);
    }

    // ---- On-the-wire request shape -----------------------------------------

    [Fact]
    public async Task ChatOptions_carries_the_thirteen_brain_tools_and_no_ResponseFormat()
    {
        ChatOptions? captured = null;
        var chat = new StubChatClient((_, opts) =>
        {
            captured = opts;
            return ToolCallResponse("Stop", ("reason", "ok"));
        });

        await new LlmAgentBrain(chat).DecideAsync(DummyState);

        Assert.NotNull(captured);
        Assert.NotNull(captured!.Tools);
        Assert.Equal(13, captured.Tools!.Count);
        var names = captured.Tools.OfType<AIFunction>().Select(f => f.Name).ToHashSet();
        Assert.Contains("Extract", names);
        Assert.Contains("Follow", names);
        Assert.Contains("Stop", names);
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
        // Brain CAN emit ActSemanticAct (the resolver cannot — fork 8).
        Assert.Contains("ActSemanticAct", names);
        // Tool-call mode: no ResponseFormat.
        Assert.Null(captured.ResponseFormat);
    }

    [Fact]
    public async Task System_prompt_pivot_no_longer_enumerates_JSON_shapes()
    {
        List<ChatMessage>? captured = null;
        var chat = new StubChatClient((msgs, _) =>
        {
            captured = msgs.ToList();
            return ToolCallResponse("Stop", ("reason", "ok"));
        });

        await new LlmAgentBrain(chat).DecideAsync(DummyState);

        var systemMsg = captured!.Single(m => m.Role == ChatRole.System).Text;
        Assert.Contains("tool", systemMsg, StringComparison.OrdinalIgnoreCase);
        // Old prose listed `"type": "extract"` shapes — those are gone.
        Assert.DoesNotContain("\"type\":", systemMsg);
    }

    [Fact]
    public async Task Prompt_includes_goal_currentUrl_candidates_and_visited()
    {
        string? capturedUserPrompt = null;
        var chat = new StubChatClient((msgs, _) =>
        {
            capturedUserPrompt = msgs.Last(m => m.Role == ChatRole.User).Text;
            return ToolCallResponse("Stop", ("reason", "ok"));
        });

        await new LlmAgentBrain(chat).DecideAsync(DummyState);

        Assert.NotNull(capturedUserPrompt);
        Assert.Contains("get all products", capturedUserPrompt);
        Assert.Contains("https://shop.example.com/", capturedUserPrompt);
        Assert.Contains("https://shop.example.com/products", capturedUserPrompt);
        Assert.Contains("# Welcome", capturedUserPrompt);
    }

    [Fact]
    public async Task LlmAgent_RunAsync_end_to_end_via_satellite_sugar()
    {
        // Pins the satellite sugar surface — the three-arg one-liner
        // constructs LlmAgentBrain internally and runs the engine.
        var chat = ToolCallStub("Stop", ("reason", "trivial"));

        var result = await LlmAgent.RunAsync(
            "https://example.com/", "test", chat,
            configure: b => b.WithPageLoader(new FakePageLoader("<html/>")));

        Assert.Equal("trivial", result.TerminationReason);
        Assert.Equal(1, result.StepsExecuted);
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

    private sealed class FakePageLoader : WebReaper.Core.Loaders.Abstract.IPageLoader
    {
        private readonly string _html;
        public FakePageLoader(string html) => _html = html;
        public Task<WebReaper.Core.Loaders.Abstract.PageLoadResult> LoadAsync(
            WebReaper.Core.Loaders.Abstract.PageRequest request,
            CancellationToken ct = default)
            => Task.FromResult(new WebReaper.Core.Loaders.Abstract.PageLoadResult { Html = _html });
    }
}
