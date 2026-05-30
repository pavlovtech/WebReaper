using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using WebReaper.AI.Http;
using Xunit;

namespace WebReaper.AI.Http.Tests;

public class OpenAiCompatibleChatClientTests
{
    private const string CannedResponse =
        """{"model":"gpt-4o-mini","choices":[{"message":{"content":"{\"title\":\"hi\"}"},"finish_reason":"stop"}],"usage":{"prompt_tokens":11,"completion_tokens":7,"total_tokens":18}}""";

    private static (OpenAiCompatibleChatClient Client, CapturingHandler Handler) NewClient(
        string? apiKey = "sk-test",
        string responseJson = CannedResponse,
        HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new CapturingHandler(responseJson, status);
        var client = new OpenAiCompatibleChatClient(
            "https://api.example.com/v1", "gpt-4o-mini", apiKey, new HttpClient(handler));
        return (client, handler);
    }

    [Fact]
    public async Task Posts_to_chat_completions_with_model_and_messages()
    {
        var (client, handler) = NewClient();
        var messages = new List<ChatMessage> { new(ChatRole.System, "sys"), new(ChatRole.User, "hello") };

        await client.GetResponseAsync(messages);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.example.com/v1/chat/completions", handler.Request.RequestUri!.ToString());
        using var doc = JsonDocument.Parse(handler.RequestBody!);
        var root = doc.RootElement;
        Assert.Equal("gpt-4o-mini", root.GetProperty("model").GetString());
        var msgs = root.GetProperty("messages");
        Assert.Equal(2, msgs.GetArrayLength());
        Assert.Equal("system", msgs[0].GetProperty("role").GetString());
        Assert.Equal("sys", msgs[0].GetProperty("content").GetString());
        Assert.Equal("user", msgs[1].GetProperty("role").GetString());
    }

    [Fact]
    public async Task Maps_temperature_max_tokens_and_json_response_format()
    {
        var (client, handler) = NewClient();
        var options = new ChatOptions
        {
            Temperature = 0.2f,
            MaxOutputTokens = 256,
            ResponseFormat = ChatResponseFormat.Json,
        };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "x")], options);

        using var doc = JsonDocument.Parse(handler.RequestBody!);
        var root = doc.RootElement;
        Assert.Equal(256, root.GetProperty("max_tokens").GetInt32());
        Assert.True(Math.Abs(root.GetProperty("temperature").GetDouble() - 0.2) < 1e-6);
        Assert.Equal("json_object", root.GetProperty("response_format").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Omits_response_format_when_not_json()
    {
        var (client, handler) = NewClient();
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "x")], new ChatOptions());
        using var doc = JsonDocument.Parse(handler.RequestBody!);
        Assert.False(doc.RootElement.TryGetProperty("response_format", out _));
    }

    [Fact]
    public async Task Parses_content_and_usage()
    {
        var (client, _) = NewClient();
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "x")]);
        Assert.Equal("""{"title":"hi"}""", response.Text);
        Assert.Equal(11, response.Usage!.InputTokenCount);
        Assert.Equal(7, response.Usage.OutputTokenCount);
        Assert.Equal(18, response.Usage.TotalTokenCount);
    }

    [Fact]
    public async Task Sends_bearer_when_api_key_present()
    {
        var (client, handler) = NewClient(apiKey: "sk-abc");
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "x")]);
        Assert.Equal("Bearer", handler.Request!.Headers.Authorization!.Scheme);
        Assert.Equal("sk-abc", handler.Request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Omits_authorization_when_api_key_absent_or_blank()
    {
        var (client, handler) = NewClient(apiKey: "   ");
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "x")]);
        Assert.Null(handler.Request!.Headers.Authorization);
    }

    [Fact]
    public async Task Throws_on_non_success_status_carrying_the_code()
    {
        var (client, _) = NewClient(responseJson: "no", status: HttpStatusCode.Unauthorized);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "x")]));
        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public async Task Throws_NotSupported_when_tools_are_present()
    {
        var (client, _) = NewClient();
        var options = new ChatOptions { Tools = [new DummyTool()] };
        await Assert.ThrowsAsync<NotSupportedException>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "x")], options));
    }

    [Fact]
    public void GetService_returns_self_for_assignable_types_only()
    {
        var (client, _) = NewClient();
        Assert.Same(client, client.GetService(typeof(IChatClient)));
        Assert.Same(client, client.GetService(typeof(OpenAiCompatibleChatClient)));
        Assert.Null(client.GetService(typeof(string)));
    }

    private sealed class CapturingHandler(string responseJson, HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            if (request.Content is not null)
                RequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class DummyTool : AIFunction
    {
        public override string Name => "dummy";
        public override JsonElement JsonSchema { get; } = JsonDocument.Parse("{}").RootElement.Clone();
        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments, CancellationToken cancellationToken)
            => ValueTask.FromResult<object?>(null);
    }
}
