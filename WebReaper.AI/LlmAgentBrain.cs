using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using WebReaper.Core.Agent.Abstract;
using WebReaper.Domain.Agent;
using WebReaper.Domain.PageActions;
using WebReaper.Domain.Parsing;

namespace WebReaper.AI;

/// <summary>
/// The LLM adapter of the <see cref="IAgentBrain"/> seam (ADR-0051) — the
/// satellite's sibling to <see cref="LlmContentExtractor"/> (ADR-0044),
/// <see cref="LlmSelectorRepairer"/> (ADR-0047), and
/// <see cref="LlmActionResolver"/> (ADR-0050). Same shape: bound to
/// <c>Microsoft.Extensions.AI.Abstractions</c>'s <see cref="IChatClient"/>,
/// the consumer brings their own concrete chat client (OpenAI, Anthropic via
/// wrapper, Ollama, anything implementing the interface).
/// <para>
/// On each <see cref="DecideAsync"/> call the brain prompts the model with
/// the bounded <see cref="AgentState"/> view (goal, current page Markdown,
/// candidate URLs, history, visited, extracted-count) and parses the
/// response as one of the four <see cref="AgentDecision"/> arms — Extract /
/// Follow / Act / Stop. The Extract arm carries a single-level Schema (the
/// v1 shape — nested Schemas are a v2 deferral); the Act arm uses the same
/// concrete-arm shape as <see cref="LlmActionResolver"/>.
/// </para>
/// </summary>
public sealed class LlmAgentBrain : IAgentBrain
{
    private const string DefaultSystemPrompt =
        "You are an autonomous web-scraping agent reasoning step-by-step on " +
        "pages of a single site to satisfy the user's goal. At each step you " +
        "observe the current page (rendered to Markdown), the candidate " +
        "links, your prior decisions, the URLs you have already visited, and " +
        "the records you have already extracted. You then decide ONE action " +
        "and return it as a JSON object — no commentary, no Markdown code " +
        "fences.\n\n" +
        "Return EXACTLY one of these JSON shapes:\n" +
        "  { \"type\": \"extract\", \"reason\": \"<why>\", \"schema\": { \"<field>\": \"<cssSelector>\", ... } }\n" +
        "  { \"type\": \"follow\",  \"reason\": \"<why>\", \"url\": \"<absolute-url-from-candidates>\" }\n" +
        "  { \"type\": \"act\",     \"reason\": \"<why>\", \"action\": { \"kind\": \"click|wait|waitFor|evaluate\", ... } }\n" +
        "  { \"type\": \"stop\",    \"reason\": \"<why-the-goal-is-met-or-unsatisfiable>\" }\n\n" +
        "Action object shapes:\n" +
        "  { \"kind\": \"click\",    \"selector\": \"<css>\" }\n" +
        "  { \"kind\": \"waitFor\",  \"selector\": \"<css>\", \"timeoutMs\": <int> }\n" +
        "  { \"kind\": \"wait\",     \"ms\":       <int>   }\n" +
        "  { \"kind\": \"evaluate\", \"expression\": \"<js>\" }\n\n" +
        "Stop when the goal is satisfied OR the page set has been exhausted " +
        "without progress. Prefer Follow over Act when a link will do — Act " +
        "is for buttons, forms, infinite-scroll triggers, etc. Pick Follow " +
        "URLs FROM the candidate list, not from memory. Don't propose a URL " +
        "you've already visited. The schema's value strings are CSS " +
        "selectors (one selector per field, single level — no nested " +
        "objects in v1).";

    private readonly IChatClient _chatClient;
    private readonly LlmAgentBrainOptions _options;

    /// <summary>Construct with an <see cref="IChatClient"/> and optional
    /// <see cref="LlmAgentBrainOptions"/>.</summary>
    public LlmAgentBrain(IChatClient chatClient, LlmAgentBrainOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _chatClient = chatClient;
        _options = options ?? new LlmAgentBrainOptions();
    }

    /// <inheritdoc/>
    public async ValueTask<AgentDecision> DecideAsync(
        AgentState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var userPrompt = BuildUserPrompt(state);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, _options.SystemPrompt ?? DefaultSystemPrompt),
            new(ChatRole.User,   userPrompt)
        };

        var chatOptions = new ChatOptions
        {
            ModelId = _options.Model,
            Temperature = _options.Temperature,
            MaxOutputTokens = _options.MaxResponseTokens,
            ResponseFormat = ChatResponseFormat.Json
        };

        var response = await _chatClient.GetResponseAsync(messages, chatOptions, cancellationToken);

        var text = StripJsonFences(response.Text ?? string.Empty);
        if (string.IsNullOrWhiteSpace(text))
            return new AgentDecision.Stop { Reason = "brain returned empty response" };

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return new AgentDecision.Stop { Reason = "brain returned non-JSON response" };
        }
        if (parsed is not JsonObject obj)
            return new AgentDecision.Stop { Reason = "brain response was not a JSON object" };

        return ParseDecision(obj);
    }

    private string BuildUserPrompt(AgentState state)
    {
        var sb = new StringBuilder();
        sb.Append("Goal: ").AppendLine(state.Goal);
        sb.Append("Step: ").Append(state.StepNumber).AppendLine();
        sb.Append("Current URL: ").AppendLine(state.CurrentUrl);
        sb.AppendLine();
        sb.Append("Records extracted so far: ").Append(state.Extracted.Count).AppendLine();
        sb.AppendLine();

        if (state.History.Count > 0)
        {
            sb.AppendLine("Recent decisions (most recent last):");
            foreach (var d in state.History)
            {
                sb.Append("  - ").Append(d.GetType().Name).Append(": ").AppendLine(d.Reason);
            }
            sb.AppendLine();
        }

        if (state.VisitedUrls.Count > 0)
        {
            sb.AppendLine("Visited URLs (do not re-Follow):");
            foreach (var u in state.VisitedUrls) sb.Append("  - ").AppendLine(u);
            sb.AppendLine();
        }

        if (state.CandidateUrls.Count > 0)
        {
            sb.AppendLine("Candidate URLs on current page:");
            foreach (var u in state.CandidateUrls) sb.Append("  - ").AppendLine(u);
            sb.AppendLine();
        }

        sb.AppendLine("Current page (Markdown, may be truncated):");
        sb.AppendLine(state.CurrentPageMarkdown);

        return sb.ToString();
    }

    private static AgentDecision ParseDecision(JsonObject obj)
    {
        var type = obj["type"]?.GetValue<string>();
        var reason = obj["reason"]?.GetValue<string>() ?? "";

        switch (type)
        {
            case "extract":
                if (obj["schema"] is not JsonObject schemaObj)
                    return new AgentDecision.Stop { Reason = $"brain Extract missing 'schema': {reason}" };
                var schema = ParseFlatSchema(schemaObj);
                if (schema.Children.Count == 0)
                    return new AgentDecision.Stop { Reason = $"brain Extract schema was empty: {reason}" };
                return new AgentDecision.Extract(schema) { Reason = reason };

            case "follow":
                var url = obj["url"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(url))
                    return new AgentDecision.Stop { Reason = $"brain Follow missing 'url': {reason}" };
                return new AgentDecision.Follow(url) { Reason = reason };

            case "act":
                if (obj["action"] is not JsonObject actionObj)
                    return new AgentDecision.Stop { Reason = $"brain Act missing 'action': {reason}" };
                var action = ParseAction(actionObj);
                if (action is null)
                    return new AgentDecision.Stop { Reason = $"brain Act had unsupported action shape: {reason}" };
                return new AgentDecision.Act(action) { Reason = reason };

            case "stop":
                return new AgentDecision.Stop { Reason = reason };

            default:
                return new AgentDecision.Stop { Reason = $"brain returned unknown decision type '{type}'" };
        }
    }

    // v1: single-level flat schema { "field": "selector", ... }. Nested
    // schemas (objects-within-objects, lists-of-objects) are a v2 deferral
    // matching ADR-0045's source-gen v2 deferral.
    private static Schema ParseFlatSchema(JsonObject schemaObj)
    {
        var s = new Schema();
        foreach (var kvp in schemaObj)
        {
            var fieldName = kvp.Key;
            var selector = kvp.Value?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(fieldName) || string.IsNullOrWhiteSpace(selector)) continue;
            s.Add(new SchemaElement(fieldName, selector));
        }
        return s;
    }

    // Same arm whitelist as LlmActionResolver (ADR-0050) so the satellite
    // resolver and the satellite brain agree on action shape grammar.
    private static PageAction? ParseAction(JsonObject obj)
    {
        var kind = obj["kind"]?.GetValue<string>();
        return kind switch
        {
            "click" when obj["selector"]?.GetValue<string>() is { Length: > 0 } sel
                => new PageAction.Click(sel),
            "waitFor" when obj["selector"]?.GetValue<string>() is { Length: > 0 } sel
                => new PageAction.WaitForSelector(sel, GetIntOrDefault(obj, "timeoutMs", 30_000)),
            "wait" => new PageAction.Wait(GetIntOrDefault(obj, "ms", 0)),
            "evaluate" when obj["expression"]?.GetValue<string>() is { Length: > 0 } expr
                => new PageAction.EvaluateExpression(expr),
            _ => null
        };
    }

    private static int GetIntOrDefault(JsonObject obj, string key, int fallback)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is null) return fallback;
        try { return node.GetValue<int>(); }
        catch { return fallback; }
    }

    private static string StripJsonFences(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;
        var newlineIdx = trimmed.IndexOf('\n');
        if (newlineIdx < 0) return trimmed;
        var body = trimmed[(newlineIdx + 1)..];
        var endIdx = body.LastIndexOf("```", StringComparison.Ordinal);
        if (endIdx > 0) body = body[..endIdx];
        return body.Trim();
    }
}
