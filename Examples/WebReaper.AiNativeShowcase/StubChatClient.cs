using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace WebReaper.AiNativeShowcase;

// Minimal IChatClient stub — returns a canned response. The real DI
// point in production is an OpenAI / Anthropic / Ollama / Azure-AI /
// xAI IChatClient adapter from the Microsoft.Extensions.AI ecosystem;
// the consumer brings their own.
internal sealed class StubChatClient : IChatClient
{
    private readonly Func<IEnumerable<ChatMessage>, string> _respond;

    public StubChatClient(Func<IEnumerable<ChatMessage>, string> respond) => _respond = respond;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var text = _respond(messages);
        var msg = new ChatMessage(ChatRole.Assistant, text);
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
