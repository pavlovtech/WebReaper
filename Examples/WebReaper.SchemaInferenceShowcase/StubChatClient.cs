using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace WebReaper.SchemaInferenceShowcase;

// Minimal IChatClient stub — returns a canned response per call (cycling
// through a list when more than one is supplied). The real DI point in
// production is an OpenAI / Anthropic / Ollama / Azure-AI IChatClient
// adapter from the Microsoft.Extensions.AI ecosystem; the consumer
// brings their own.
internal sealed class StubChatClient : IChatClient
{
    private readonly string[] _responses;
    private int _calls;

    public StubChatClient(params string[] responses) => _responses = responses;
    public int Calls => _calls;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var idx = Math.Min(_calls, _responses.Length - 1);
        Interlocked.Increment(ref _calls);
        var msg = new ChatMessage(ChatRole.Assistant, _responses[idx]);
        return Task.FromResult(new ChatResponse(msg));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) => Empty(cancellationToken);

    private static async IAsyncEnumerable<ChatResponseUpdate> Empty(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        yield break;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}
