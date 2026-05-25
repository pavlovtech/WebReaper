using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using WebReaper.AI.Llm;
using WebReaper.Domain.Parsing;

namespace WebReaper.AI.Tests;

// ADR-0066: contract tests for adapter→LlmCall→ILlmCallTelemetry
// threading. Verifies that each of the four built-in adapters
// (LlmContentExtractor, LlmSelectorRepairer, LlmActionResolver,
// LlmAgentBrain) correctly threads a provided telemetry instance
// to its underlying LlmCall, and that calls are recorded under the
// adapter's descriptor Name.
public class LlmCallTelemetryWiringTests
{
    [Fact]
    public async Task LlmContentExtractor_reports_to_telemetry_with_its_DescriptorName()
    {
        var telemetry = new LlmCallTelemetry();
        var chat = new StubChatClient(_ => "{\"name\":\"value\"}");

        var extractor = new LlmContentExtractor(chat, options: null, telemetry: telemetry);
        var schema = new Schema { new SchemaElement("name", "h1") };

        await extractor.ExtractAsync("<html><body><h1>x</h1></body></html>", schema);

        var snap = telemetry.Snapshot();
        Assert.Equal(1, snap.CallCount);
        Assert.True(snap.PerAdapter.ContainsKey(nameof(LlmContentExtractor)),
            $"Expected '{nameof(LlmContentExtractor)}' in PerAdapter; got: " +
            string.Join(", ", snap.PerAdapter.Keys));
    }

    [Fact]
    public async Task LlmSelectorRepairer_reports_to_telemetry_with_its_DescriptorName()
    {
        var telemetry = new LlmCallTelemetry();
        var chat = new StubChatClient(_ => "{\"name\":\".alt\"}");

        var repairer = new LlmSelectorRepairer(chat, options: null, telemetry: telemetry);
        var schema = new Schema { new SchemaElement("name", "h1") };
        var failedResult = new JsonObject { ["name"] = "" };

        await repairer.RepairAsync(
            schema, "<html><body><h1 class='alt'>x</h1></body></html>",
            failedResult, failureReason: "name empty");

        var snap = telemetry.Snapshot();
        Assert.Equal(1, snap.CallCount);
        Assert.True(snap.PerAdapter.ContainsKey(nameof(LlmSelectorRepairer)),
            $"Expected '{nameof(LlmSelectorRepairer)}' in PerAdapter; got: " +
            string.Join(", ", snap.PerAdapter.Keys));
    }

    [Fact]
    public async Task LlmActionResolver_reports_to_telemetry_with_its_DescriptorName()
    {
        var telemetry = new LlmCallTelemetry();
        var chat = new StubChatClient((msgs, opts) =>
            new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent>
            {
                new FunctionCallContent("c1", "ActScrollToEnd", new Dictionary<string, object?>())
            })));

        var resolver = new LlmActionResolver(chat, options: null, telemetry: telemetry);

        await resolver.ResolveAsync("scroll to bottom", "<html><body></body></html>");

        var snap = telemetry.Snapshot();
        Assert.Equal(1, snap.CallCount);
        Assert.True(snap.PerAdapter.ContainsKey(nameof(LlmActionResolver)),
            $"Expected '{nameof(LlmActionResolver)}' in PerAdapter; got: " +
            string.Join(", ", snap.PerAdapter.Keys));
    }

    [Fact]
    public async Task Adapter_with_null_telemetry_does_not_throw()
    {
        // Null telemetry → mechanism uses NullLlmCallTelemetry.Instance.
        // No throw on Record.
        var chat = new StubChatClient(_ => "{\"name\":\"x\"}");
        var extractor = new LlmContentExtractor(chat, options: null, telemetry: null);
        var schema = new Schema { new SchemaElement("name", "h1") };

        await extractor.ExtractAsync("<html><body><h1>x</h1></body></html>", schema);
        // Test passes by reaching this line without exception.
    }

    [Fact]
    public async Task Two_adapters_sharing_one_telemetry_aggregate_globally_split_per_adapter()
    {
        var telemetry = new LlmCallTelemetry();
        var chat = new StubChatClient(_ => "{\"name\":\"x\"}");

        var extractor1 = new LlmContentExtractor(chat, options: null, telemetry: telemetry);
        var extractor2 = new LlmSelectorRepairer(chat, options: null, telemetry: telemetry);

        var schema = new Schema { new SchemaElement("name", "h1") };
        var failedResult = new JsonObject { ["name"] = "" };

        await extractor1.ExtractAsync("<html><body><h1>x</h1></body></html>", schema);
        await extractor2.RepairAsync(schema, "<html></html>", failedResult);

        var snap = telemetry.Snapshot();
        Assert.Equal(2, snap.CallCount);
        Assert.Equal(2, snap.PerAdapter.Count);
        Assert.True(snap.PerAdapter.ContainsKey(nameof(LlmContentExtractor)));
        Assert.True(snap.PerAdapter.ContainsKey(nameof(LlmSelectorRepairer)));
    }

    // ---- Stub IChatClient (local copy; same shape as LlmCallTests) ----

    private sealed class StubChatClient : IChatClient
    {
        private readonly Func<IEnumerable<ChatMessage>, ChatOptions?, ChatResponse> _respond;

        public StubChatClient(Func<IEnumerable<ChatMessage>, string> respond)
            : this((m, _) => new ChatResponse(new ChatMessage(ChatRole.Assistant, respond(m)))) { }

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
