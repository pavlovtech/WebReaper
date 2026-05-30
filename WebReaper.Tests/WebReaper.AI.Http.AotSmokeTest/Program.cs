// ADR-0084 AOT smoke test for WebReaper.AI.Http. Under PublishAot with the
// IL2026/IL3050 family promoted to errors, exercises the client end to end
// against a stub HttpMessageHandler: serialize the chat request and
// deserialize the chat response through the System.Text.Json source-gen
// context. No network. Exits non-zero on any assertion failure; the build
// fails on any trim/AOT warning.

using System.Net;
using System.Text;
using Microsoft.Extensions.AI;
using WebReaper.AI.Http;

var failures = new List<string>();
void Check(bool ok, string label)
{
    Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {label}");
    if (!ok) failures.Add(label);
}

using var client = new OpenAiCompatibleChatClient(
    "https://stub.local/v1", "test-model", "sk-x", new HttpClient(new StubHandler()));

var response = await client.GetResponseAsync(
    [new ChatMessage(ChatRole.System, "s"), new ChatMessage(ChatRole.User, "u")],
    new ChatOptions { Temperature = 0f, MaxOutputTokens = 64, ResponseFormat = ChatResponseFormat.Json });

Check(response.Text == "{\"ok\":true}", "request serialize + response content parse");
Check(response.Usage?.TotalTokenCount == 3, "usage parse");

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} smoke failure(s).");
    return 1;
}

Console.WriteLine("WebReaper.AI.Http AOT smoke: all PASS");
return 0;

sealed class StubHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"model\":\"test-model\",\"choices\":[{\"message\":" +
                "{\"content\":\"{\\\"ok\\\":true}\"}}]," +
                "\"usage\":{\"prompt_tokens\":2,\"completion_tokens\":1,\"total_tokens\":3}}",
                Encoding.UTF8, "application/json"),
        });
}
