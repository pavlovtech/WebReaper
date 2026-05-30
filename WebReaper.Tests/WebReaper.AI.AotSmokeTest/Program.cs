// ADR-0084 AOT smoke test for the WebReaper.AI satellite. Exercises the two
// LLM code paths under PublishAot with the IL2026/IL3050 family promoted to
// build errors:
//   1. JSON-mode extraction (LlmContentExtractor) -> roots the
//      LlmCallDescriptor JsonSerializerOptions default (Site 1).
//   2. Tool-call dispatch (LlmCall<T> in tool mode) -> roots the tool
//      arguments-dictionary to JsonElement conversion (Site 2).
// A stub IChatClient drives both paths; no network. Exits non-zero on any
// assertion failure; the build fails on any trim/AOT warning.

using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using WebReaper.AI;
using WebReaper.AI.Llm;
using WebReaper.Domain.Parsing;

var failures = new List<string>();
void Check(bool ok, string label)
{
    Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {label}");
    if (!ok) failures.Add(label);
}

var stub = new StubChatClient();

// 1. JSON-mode path: the LLM extractor. Constructing it roots the
//    LlmCallDescriptor default options (Site 1); running it roots the
//    Markdown pre-clean + JSON parse.
var extractor = new LlmContentExtractor(stub);
var schema = new Schema { new SchemaElement("title", "h1") };
var record = await extractor.ExtractAsync("<html><body><h1>hello</h1></body></html>", schema);
Check(record is not null, "LlmContentExtractor JSON-mode path");

// 1b. ADR-0084 piece 3: schema-free prompt extraction (null schema is valid).
var promptExtractor = new PromptContentExtractor(stub, "extract the title");
var promptRecord = await promptExtractor.ExtractAsync("<html><body><h1>hello</h1></body></html>", null);
Check(promptRecord is not null, "PromptContentExtractor schema-free path");

// 2. Tool-call path: an LlmCall<T> in tool mode. The stub returns a
//    FunctionCallContent whose argument is a JsonElement (the real shape a
//    chat client produces), so this roots and exercises the args-dictionary
//    to JsonElement conversion (Site 2 - what ADR-0084 makes reflection-free).
var toolDescriptor = new LlmCallDescriptor<string>
{
    SystemPrompt = "system",
    BuildUserMessage = _ => "user",
    ParseResponse = _ => "json-mode-unused",
    Tools = new AIFunction[] { new SchemaOnlyAIFunction() },
    ParseToolCall = (name, args) => $"{name}:{args.GetRawText()}",
};
var toolCall = new LlmCall<string>(stub, toolDescriptor);
var toolResult = await toolCall.InvokeAsync(new object());
Check(toolResult.Value is not null && toolResult.Value.Contains("hello"),
    "LlmCall tool-call argument conversion");

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} smoke failure(s).");
    return 1;
}

Console.WriteLine("WebReaper.AI AOT smoke: all PASS");
return 0;

// A do-nothing IChatClient: JSON text when no tools are offered, a
// FunctionCallContent with a JsonElement argument when tools are offered.
sealed class StubChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (options?.Tools is { Count: > 0 })
        {
            var argValue = JsonDocument.Parse("\"hello\"").RootElement.Clone();
            var call = new FunctionCallContent(
                "call-1", "extract",
                new Dictionary<string, object?> { ["query"] = argValue });
            var message = new ChatMessage(ChatRole.Assistant, new List<AIContent> { call });
            return Task.FromResult(new ChatResponse(message));
        }

        return Task.FromResult(new ChatResponse(
            new ChatMessage(ChatRole.Assistant, "{\"title\":\"hello\"}")));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => Empty(cancellationToken);

    private static async IAsyncEnumerable<ChatResponseUpdate> Empty(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        yield break;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}

// A minimal AOT-friendly AIFunction carrying a pre-built schema (mirrors the
// satellite's internal HandRolledAIFunction, which the test assembly cannot
// see). The SDK never invokes it; it only goes on ChatOptions.Tools.
sealed class SchemaOnlyAIFunction : AIFunction
{
    private readonly JsonElement _schema;
    public override string Name => "extract";
    public override string Description => "smoke-test tool";
    public override JsonElement JsonSchema => _schema;

    public SchemaOnlyAIFunction()
    {
        using var doc = JsonDocument.Parse("{\"type\":\"object\"}");
        _schema = doc.RootElement.Clone();
    }

    protected override ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
        => ValueTask.FromResult<object?>(null);
}
