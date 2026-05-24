using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using WebReaper.AI;
using WebReaper.Domain.Parsing;

namespace WebReaper.AI.Tests;

// ADR-0047 + ADR-0062: the LLM-backed selector repairer. Tests use a
// stub IChatClient — no real model call — so we can pin the prompt
// composition, the JSON response parsing, and crucially the
// failureReason threading from the ISchemaValidator seam into the
// repairer prompt.
public class LlmSelectorRepairerTests
{
    [Fact]
    public async Task Returns_patched_schema_when_model_proposes_new_selector()
    {
        var chat = new StubChatClient(_ => "{\"title\":\".new-h1\"}");

        var original = new Schema { new SchemaElement("title", ".old-h1", DataType.String) };
        var failed = new JsonObject { ["title"] = "" };

        var patched = await new LlmSelectorRepairer(chat).RepairAsync(
            original, "<html/>", failed);

        Assert.NotNull(patched);
        var leaf = Assert.IsType<SchemaElement>(patched!.Children[0]);
        Assert.Equal("title", leaf.Field);
        Assert.Equal(".new-h1", leaf.Selector);
    }

    [Fact]
    public async Task Returns_null_when_model_returns_empty_object()
    {
        var chat = new StubChatClient(_ => "{}");

        var original = new Schema { new SchemaElement("title", ".old-h1", DataType.String) };
        var failed = new JsonObject { ["title"] = "" };

        var patched = await new LlmSelectorRepairer(chat).RepairAsync(
            original, "<html/>", failed);

        Assert.Null(patched);
    }

    [Fact]
    public async Task Returns_null_when_model_returns_malformed_json()
    {
        var chat = new StubChatClient(_ => "this is not json");

        var original = new Schema { new SchemaElement("title", ".old-h1", DataType.String) };

        var patched = await new LlmSelectorRepairer(chat).RepairAsync(
            original, "<html/>", new JsonObject());

        Assert.Null(patched);
    }

    [Fact]
    public async Task Failure_reason_when_supplied_appears_in_the_user_prompt()
    {
        // ADR-0062: the wrapper passes the validator's Reason; the
        // repairer must inject it into the prompt so the model sees
        // which fields the validator flagged.
        string? capturedUserPrompt = null;
        var chat = new StubChatClient((messages, _) =>
        {
            capturedUserPrompt = messages.Single(m => m.Role == ChatRole.User).Text;
            return "{\"title\":\".new-h1\"}";
        });

        var original = new Schema { new SchemaElement("title", ".old-h1", DataType.String) };
        var failed = new JsonObject { ["title"] = "" };
        const string reason = "required field 'title' is empty";

        await new LlmSelectorRepairer(chat).RepairAsync(
            original, "<html/>", failed, failureReason: reason);

        Assert.NotNull(capturedUserPrompt);
        Assert.Contains("Validator report:", capturedUserPrompt!);
        Assert.Contains(reason, capturedUserPrompt!);
    }

    [Fact]
    public async Task Null_failure_reason_omits_validator_report_section()
    {
        // Backward compat: the parameter is optional. With null, the
        // prompt simply omits the hint header — no broken formatting.
        string? capturedUserPrompt = null;
        var chat = new StubChatClient((messages, _) =>
        {
            capturedUserPrompt = messages.Single(m => m.Role == ChatRole.User).Text;
            return "{\"title\":\".new-h1\"}";
        });

        var original = new Schema { new SchemaElement("title", ".old-h1", DataType.String) };
        var failed = new JsonObject { ["title"] = "" };

        await new LlmSelectorRepairer(chat).RepairAsync(
            original, "<html/>", failed, failureReason: null);

        Assert.NotNull(capturedUserPrompt);
        Assert.DoesNotContain("Validator report:", capturedUserPrompt!);
        // Original selectors and failed result still in the prompt.
        Assert.Contains(".old-h1", capturedUserPrompt!);
    }

    [Fact]
    public async Task Whitespace_failure_reason_is_treated_as_omitted()
    {
        // Defensive: "   " is not a hint — omit the section.
        string? capturedUserPrompt = null;
        var chat = new StubChatClient((messages, _) =>
        {
            capturedUserPrompt = messages.Single(m => m.Role == ChatRole.User).Text;
            return "{}";
        });

        var original = new Schema { new SchemaElement("title", ".old-h1", DataType.String) };
        await new LlmSelectorRepairer(chat).RepairAsync(
            original, "<html/>", new JsonObject(), failureReason: "   ");

        Assert.NotNull(capturedUserPrompt);
        Assert.DoesNotContain("Validator report:", capturedUserPrompt!);
    }

    [Fact]
    public async Task Default_invocation_without_failureReason_still_works()
    {
        // The original ADR-0047 caller path: no validator hint. The
        // optional parameter defaults to null, the prompt omits the
        // section, the rest of the flow is unchanged.
        var chat = new StubChatClient(_ => "{\"title\":\".new-h1\"}");

        var original = new Schema { new SchemaElement("title", ".old-h1", DataType.String) };
        var failed = new JsonObject { ["title"] = "" };

        // No failureReason argument — using the default.
        var patched = await new LlmSelectorRepairer(chat).RepairAsync(
            original, "<html/>", failed);

        Assert.NotNull(patched);
    }

    [Fact]
    public async Task Constructor_rejects_null_chat_client()
    {
        await Task.CompletedTask;
        Assert.Throws<ArgumentNullException>(() => new LlmSelectorRepairer(null!));
    }

    [Fact]
    public async Task RepairAsync_rejects_null_original_schema()
    {
        var chat = new StubChatClient(_ => "{}");
        var repairer = new LlmSelectorRepairer(chat);
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => repairer.RepairAsync(null!, "<html/>", new JsonObject()));
    }

    [Fact]
    public async Task RepairAsync_rejects_null_document()
    {
        var chat = new StubChatClient(_ => "{}");
        var repairer = new LlmSelectorRepairer(chat);
        var original = new Schema { new SchemaElement("title", ".h1", DataType.String) };
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => repairer.RepairAsync(original, null!, new JsonObject()));
    }

    [Fact]
    public async Task RepairAsync_rejects_null_failed_result()
    {
        var chat = new StubChatClient(_ => "{}");
        var repairer = new LlmSelectorRepairer(chat);
        var original = new Schema { new SchemaElement("title", ".h1", DataType.String) };
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => repairer.RepairAsync(original, "<html/>", null!));
    }

    // Stub IChatClient — same shape as the other satellite tests'
    // stub, kept local for symmetry; each test class owns its seam.
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
