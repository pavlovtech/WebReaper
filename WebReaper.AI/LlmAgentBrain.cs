using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using WebReaper.AI.Llm;
using WebReaper.AI.Tools;
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
/// candidate URLs, history, visited, extracted-count, last outcome) and
/// translates the model's tool call into one of the four
/// <see cref="AgentDecision"/> arms — Extract / Follow / Act / Stop —
/// per ADR-0060. The closed-sum is load-bearing at the LLM boundary:
/// the registered 10-tool registry IS the schema; the SDK validates the
/// per-arm args against the per-arm schema before they reach
/// <see cref="ParseDecisionTool"/>. JSON-mode parsing is gone for this
/// adapter (ADR-0060 §Decision §5).
/// </para>
/// <para>
/// Internally delegates to <see cref="LlmCall{TResponse}"/> (ADR-0059) with
/// <c>Tools</c> + <c>ParseToolCall</c> set; the mechanism takes the tool-call
/// path automatically.
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
        "previous decision. You then call EXACTLY ONE of the provided tools " +
        "to indicate your next step. Always supply a 'reason' explaining " +
        "your choice.\n\n" +
        "Prefer Follow over an Act* tool when a link will do. Pick Follow " +
        "URLs FROM the candidate list, not from memory. Don't propose a URL " +
        "you've already visited. Use Extract when the current page contains " +
        "the records you want. Use one of the Act* tools (or ActSemanticAct " +
        "for natural-language intents) when the page needs a click, wait, " +
        "scroll, or JS evaluation to reveal content. Use Stop when the goal " +
        "is satisfied OR the page set has been exhausted without progress.\n\n" +
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
    /// <param name="chatClient">The Microsoft.Extensions.AI chat client.</param>
    /// <param name="options">Optional <see cref="LlmAgentBrainOptions"/>.</param>
    /// <param name="telemetry">Optional <see cref="ILlmCallTelemetry"/>
    /// (ADR-0066). Threaded by <c>.UseAi(...)</c> / <c>WithLlmBrain</c>
    /// from the builder; à la carte construction defaults to the null
    /// implementation.</param>
    public LlmAgentBrain(
        IChatClient chatClient,
        LlmAgentBrainOptions? options = null,
        ILlmCallTelemetry? telemetry = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        var opts = options ?? new LlmAgentBrainOptions();
        _call = new LlmCall<AgentDecision>(chatClient, new LlmCallDescriptor<AgentDecision>
        {
            Name = nameof(LlmAgentBrain),
            SystemPrompt = opts.SystemPrompt ?? DefaultSystemPrompt,
            BuildUserMessage = input => BuildUserPrompt((AgentState)input),
            // Unused in tool-call mode (the mechanism never invokes
            // ParseResponse when Tools is set); kept as a structural
            // requirement of the descriptor record. Pinning to a
            // throwing default surfaces a mechanism bug loudly.
            ParseResponse = _ => throw new InvalidOperationException(
                "LlmAgentBrain is tool-call mode; ParseResponse must not be called."),
            Tools = AgentDecisionTools.ForBrain(),
            ParseToolCall = ParseDecisionTool,
            Model = opts.Model,
            Temperature = opts.Temperature,
            MaxResponseTokens = opts.MaxResponseTokens,
            SystemPromptCache = opts.CachePolicy ?? CachePolicy.Default,
        }, telemetry: telemetry);
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
            // ADR-0060 fork 5: model returned no tool call (or the retry
            // also failed); default to Stop with a structural reason —
            // matches the existing parse-failure shape on this adapter.
            return new AgentDecision.Stop { Reason = $"brain returned no tool call: {ex.Message}" };
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

    // ADR-0060 amendment (2026-05-28): ParseToolCall delegate. Each per-arm
    // case dispatches to the arm-local FromArguments factory (in
    // PageActionTools.cs / AgentDecisionToolFragments.cs); a successful arm
    // becomes the decision verbatim (AgentDecision arms) or is wrapped in
    // Act (PageAction arms); a failure becomes Stop with the factory's
    // FailureReason composed into the audit-trail string. Unknown tool
    // name -> Stop with structural reason. The closed sum is load-bearing
    // at the LLM boundary — the model picked from a list of ten arm-shaped
    // tools; "the model emitted a JSON object with an unknown
    // discriminator" is structurally impossible.
    private static AgentDecision ParseDecisionTool(string toolName, JsonElement args)
    {
        var reason = LlmToolArguments.TryGetString(args, "reason") ?? "";

        return toolName switch
        {
            AgentDecisionTools.Extract.Name => Unwrap(AgentDecisionTools.Extract.FromArguments(args, reason), toolName, reason),
            AgentDecisionTools.Follow.Name => Unwrap(AgentDecisionTools.Follow.FromArguments(args, reason), toolName, reason),
            AgentDecisionTools.Stop.Name => Unwrap(AgentDecisionTools.Stop.FromArguments(args, reason), toolName, reason),

            PageActionTools.Click.Name => UnwrapAct(PageActionTools.Click.FromArguments(args), toolName, reason),
            PageActionTools.Wait.Name => UnwrapAct(PageActionTools.Wait.FromArguments(args), toolName, reason),
            PageActionTools.WaitForSelector.Name => UnwrapAct(PageActionTools.WaitForSelector.FromArguments(args), toolName, reason),
            PageActionTools.WaitForNetworkIdle.Name => UnwrapAct(PageActionTools.WaitForNetworkIdle.FromArguments(args), toolName, reason),
            PageActionTools.ScrollToEnd.Name => UnwrapAct(PageActionTools.ScrollToEnd.FromArguments(args), toolName, reason),
            PageActionTools.ScrollIntoView.Name => UnwrapAct(PageActionTools.ScrollIntoView.FromArguments(args), toolName, reason),
            PageActionTools.EvaluateExpression.Name => UnwrapAct(PageActionTools.EvaluateExpression.FromArguments(args), toolName, reason),
            PageActionTools.SemanticAct.Name => UnwrapAct(PageActionTools.SemanticAct.FromArguments(args), toolName, reason),
            PageActionTools.Press.Name => UnwrapAct(PageActionTools.Press.FromArguments(args), toolName, reason),

            _ => new AgentDecision.Stop
            {
                Reason = $"brain called unregistered tool '{toolName}'"
            },
        };
    }

    // Brain wrap for AgentDecision arms: the factory returns the arm with
    // its Reason already populated (the brain passed reason in); on failure
    // the freeform FailureReason composes into a Stop matching the
    // pre-amendment audit-trail format byte-for-byte.
    private static AgentDecision Unwrap<T>(ToolCallResult<T> result, string toolName, string reason)
        where T : AgentDecision =>
        result.Value is { } arm
            ? arm
            : new AgentDecision.Stop { Reason = $"brain {toolName} {result.FailureReason}: {reason}" };

    // Brain wrap for Act* arms: the factory returns the PageAction value;
    // the brain wraps in Act with the audit-trail Reason. Failure -> Stop
    // with the factory's FailureReason in the same format as the
    // pre-amendment parser.
    private static AgentDecision UnwrapAct<T>(ToolCallResult<T> result, string toolName, string reason)
        where T : PageAction =>
        result.Value is { } action
            ? new AgentDecision.Act(action) { Reason = reason }
            : new AgentDecision.Stop { Reason = $"brain {toolName} {result.FailureReason}: {reason}" };

}
