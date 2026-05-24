using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using WebReaper.AI.Llm;
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
/// Follow / Act / Stop. Internally delegates to <see cref="LlmCall{TResponse}"/>
/// (ADR-0059); the JSON discriminator shape stays the same — the pivot to
/// tool-calling is ADR-0060's job.
/// </para>
/// </summary>
public sealed class LlmAgentBrain : IAgentBrain
{
    private const string DefaultSystemPrompt =
        "You are an autonomous web-scraping agent reasoning step-by-step on " +
        "pages of a single site to satisfy the user's goal. At each step you " +
        "observe the current page (rendered to Markdown), the candidate " +
        "links, your prior decisions, the URLs you have already visited, " +
        "the records you have already extracted, and the outcome of your " +
        "previous decision. You then decide ONE action and return it as a " +
        "JSON object — no commentary, no Markdown code fences.\n\n" +
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
        "objects in v1).\n\n" +
        "The 'Last outcome' line tells you what happened when the engine " +
        "executed your previous decision:\n" +
        "  - None             — first step; you have no prior outcome.\n" +
        "  - Extracted        — the record was emitted; the count is the running total. If the count didn't grow, the processor pipeline dropped the record; consider a different schema next time.\n" +
        "  - Followed         — the page loaded; actualUrl may differ from your proposed URL after redirects; statusCode is the HTTP status (0 means a dynamic browser page with no single per-page status).\n" +
        "  - ActDispatched    — the action ran; resolvedAction shows what your intent became.\n" +
        "  - Failed           — the decision failed; reason and exceptionType describe the failure class.\n" +
        "If the last decision Failed, prefer a different approach: a Failed " +
        "Extract with 'validation: ...' means the schema didn't yield " +
        "useful fields on this page — pick a different schema or a " +
        "different URL. A Failed Follow with '404' or 'load: ...' means " +
        "the URL didn't load — don't re-propose it. A Failed Act means the " +
        "page didn't accept the action — pick a different selector or move " +
        "on. Don't repeat a decision the engine just told you failed.";

    private readonly LlmCall<AgentDecision> _call;

    /// <summary>Construct with an <see cref="IChatClient"/> and optional
    /// <see cref="LlmAgentBrainOptions"/>.</summary>
    public LlmAgentBrain(IChatClient chatClient, LlmAgentBrainOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        var opts = options ?? new LlmAgentBrainOptions();
        _call = new LlmCall<AgentDecision>(chatClient, new LlmCallDescriptor<AgentDecision>
        {
            Name = nameof(LlmAgentBrain),
            SystemPrompt = opts.SystemPrompt ?? DefaultSystemPrompt,
            BuildUserMessage = input => BuildUserPrompt((AgentState)input),
            ParseResponse = ParseDecisionElement,
            Model = opts.Model,
            Temperature = opts.Temperature,
            MaxResponseTokens = opts.MaxResponseTokens,
        });
    }

    /// <inheritdoc/>
    public async ValueTask<AgentDecision> DecideAsync(
        AgentState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        try
        {
            var result = await _call.InvokeAsync(state, cancellationToken);
            return result.Value;
        }
        catch (LlmCallException ex)
        {
            return new AgentDecision.Stop { Reason = $"brain returned non-JSON response: {ex.Message}" };
        }
    }

    private static string BuildUserPrompt(AgentState state)
    {
        var sb = new StringBuilder();
        sb.Append("Goal: ").AppendLine(state.Goal);
        sb.Append("Step: ").Append(state.StepNumber).AppendLine();
        sb.Append("Current URL: ").AppendLine(state.CurrentUrl);
        sb.AppendLine();
        sb.Append("Records extracted so far: ").Append(state.Extracted.Count).AppendLine();
        // ADR-0061: surface the engine's per-step outcome so the brain can
        // see what happened on the previous decision and avoid re-running a
        // failing decision. One compact line.
        sb.Append("Last outcome: ").AppendLine(FormatOutcome(state.LastOutcome));
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

    // ADR-0061: compact one-line representation of the outcome closed sum
    // for the brain prompt. Records are stringified to a short JSON
    // excerpt — the brain has the cumulative Extracted list already; this
    // line is the *signal* about the just-executed step, not the data.
    private static string FormatOutcome(AgentDecisionOutcome outcome) => outcome switch
    {
        AgentDecisionOutcome.None => "None (first step)",
        AgentDecisionOutcome.Extracted x =>
            x.Record is null
                ? $"Extracted (record dropped by processor; count={x.RecordCount})"
                : $"Extracted (count={x.RecordCount}; record={x.Record.ToJsonString()})",
        AgentDecisionOutcome.Followed x =>
            $"Followed (actualUrl={x.ActualUrl}; statusCode={x.StatusCode})",
        AgentDecisionOutcome.ActDispatched x =>
            $"ActDispatched (resolvedAction={x.ResolvedAction.GetType().Name})",
        AgentDecisionOutcome.Failed x =>
            x.ExceptionType is null
                ? $"Failed (reason={x.Reason})"
                : $"Failed (reason={x.Reason}; exceptionType={x.ExceptionType})",
        AgentDecisionOutcome.Stopped x => $"Stopped (reason={x.Reason})",
        _ => "Unknown"
    };

    // The descriptor's ParseResponse: JsonElement → AgentDecision. A non-
    // object element, or a malformed arm, throws — the mechanism translates
    // it to LlmCallException (after retry), which DecideAsync catches and
    // returns AgentDecision.Stop for.
    private static AgentDecision ParseDecisionElement(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException(
                $"brain response was not a JSON object (kind={element.ValueKind})");

        // Round-trip into a mutable JsonObject for the existing arm-parsing
        // shape — preserves the per-arm validation discipline.
        var node = JsonNode.Parse(element.GetRawText())
            ?? throw new InvalidOperationException("brain response parsed to null");
        if (node is not JsonObject obj)
            throw new InvalidOperationException("brain response was not a JSON object");

        return ParseDecision(obj);
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
}
