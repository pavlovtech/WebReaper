using System.Text.Json;
using Microsoft.Extensions.AI;
using WebReaper.AI.Llm;
using WebReaper.AI.Tools;
using WebReaper.Core.Actions.Abstract;
using WebReaper.Domain.PageActions;

namespace WebReaper.AI;

/// <summary>
/// The LLM adapter of the <see cref="IActionResolver"/> seam (ADR-0050) — the
/// satellite's sibling to <see cref="LlmContentExtractor"/> and
/// <see cref="LlmSelectorRepairer"/>, applied to the *action* surface instead
/// of the *extraction* surface. Bound to
/// <c>Microsoft.Extensions.AI.Abstractions</c>'s
/// <see cref="IChatClient"/> — the consumer brings their own concrete chat
/// client (OpenAI, Anthropic via wrapper, Ollama, anything implementing the
/// interface).
/// <para>
/// Asked once per intent string per crawl: the model calls EXACTLY ONE of the
/// six concrete action tools (<c>ActClick</c>, <c>ActWait</c>,
/// <c>ActWaitForSelector</c>, <c>ActWaitForNetworkIdle</c>,
/// <c>ActScrollToEnd</c>, <c>ActEvaluate</c>) per ADR-0060. The resolver
/// constructs the matching <see cref="PageAction"/> arm; the Puppeteer
/// transport caches it and dispatches the cached arm on every subsequent
/// same-intent invocation (the LLM-as-proposer / deterministic-as-decider
/// pattern, ADR-0046 / ADR-0047 generalised to actions).
/// </para>
/// <para>
/// The resolver's tool registry has seven arms (ADR-0074 adds
/// <c>ActScrollIntoView</c>); never <c>ActSemanticAct</c>
/// (fork 8 verdict: the closed sum is closed at the resolver's tool list,
/// structurally preventing the resolver from looping the transport's
/// resolution path). Unknown tool name returns <c>null</c>; the transport
/// translates that to
/// <see cref="WebReaper.Core.Actions.Concrete.SemanticActResolutionException"/>.
/// </para>
/// <para>
/// Internally delegates to <see cref="LlmCall{TResponse}"/> (ADR-0059) — the
/// transport, the bounded retry, and <see cref="ChatResponse.Usage"/> capture
/// all live there.
/// </para>
/// </summary>
public sealed class LlmActionResolver : IActionResolver
{
    // ADR-0060: post tool-calling pivot the tool list IS the schema, so the
    // prompt no longer enumerates JSON shapes ({ "kind": ... }) — the provided
    // action tools and their parameter schemas define the concrete shapes. The
    // model just picks one tool. Behavioural pin: AgentDecisionToolsTests /
    // LlmActionResolverTests.System_prompt_no_longer_enumerates_JSON_shapes.
    private const string DefaultSystemPrompt =
        "You are resolving a user's natural-language intent to a concrete " +
        "browser action on the supplied HTML page. Call EXACTLY ONE of the " +
        "provided action tools to indicate the concrete action; the tool list " +
        "is the schema. Pick the simplest action that satisfies the intent. " +
        "Prefer a CSS selector specific enough not to collide with other " +
        "elements (prefer id over class, class over tag; combine if needed). " +
        "Use the fill tool when the intent is to type text into an input, " +
        "textarea, or content-editable element; it clears any existing value " +
        "before inserting the new text.";

    private readonly LlmCall<PageAction?> _call;
    private readonly LlmActionResolverOptions _options;

    /// <summary>Construct with an <see cref="IChatClient"/> and optional
    /// <see cref="LlmActionResolverOptions"/>.</summary>
    /// <param name="chatClient">The Microsoft.Extensions.AI chat client.</param>
    /// <param name="options">Optional <see cref="LlmActionResolverOptions"/>.</param>
    /// <param name="telemetry">Optional <see cref="ILlmCallTelemetry"/>
    /// (ADR-0066). Threaded by <c>.UseAi(...)</c> / <c>WithLlm*</c> from
    /// the builder; à la carte construction defaults to the null
    /// implementation.</param>
    public LlmActionResolver(
        IChatClient chatClient,
        LlmActionResolverOptions? options = null,
        ILlmCallTelemetry? telemetry = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _options = options ?? new LlmActionResolverOptions();
        _call = new LlmCall<PageAction?>(chatClient, new LlmCallDescriptor<PageAction?>
        {
            Name = nameof(LlmActionResolver),
            SystemPrompt = _options.SystemPrompt ?? DefaultSystemPrompt,
            BuildUserMessage = input => BuildUserPrompt((ResolveInput)input),
            // Unused in tool-call mode; pinning to a throwing default
            // surfaces a mechanism bug loudly.
            ParseResponse = _ => throw new InvalidOperationException(
                "LlmActionResolver is tool-call mode; ParseResponse must not be called."),
            Tools = AgentDecisionTools.ForResolver(),
            ParseToolCall = ParseActionTool,
            Model = _options.Model,
            Temperature = _options.Temperature,
            MaxResponseTokens = _options.MaxResponseTokens,
            SystemPromptCache = _options.CachePolicy ?? CachePolicy.Default,
        }, telemetry: telemetry);
    }

    /// <inheritdoc/>
    public async Task<PageAction?> ResolveAsync(
        string intent,
        string pageHtml,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intent);
        ArgumentNullException.ThrowIfNull(pageHtml);

        var trimmedHtml = pageHtml.Length > _options.MaxHtmlChars
            ? pageHtml[.._options.MaxHtmlChars]
            : pageHtml;

        var input = new ResolveInput(intent, trimmedHtml);

        try
        {
            var result = await _call.InvokeAsync(input, cancellationToken);
            return result.Value;
        }
        catch (LlmCallException)
        {
            // Adapter policy: tool-call-after-retry failure (model
            // returned no FunctionCallContent twice in a row) -> null.
            // The transport translates that to
            // SemanticActResolutionException.
            return null;
        }
    }

    private static string BuildUserPrompt(ResolveInput input) =>
        "Intent: " + input.Intent + "\n\n" +
        "Page (HTML, may be truncated):\n" + input.Html;

    // ADR-0060 fork 8 + ADR-0074: the resolver's tool list has seven arms
    // (ScrollIntoView added); the closed sum is closed structurally (no
    // ActSemanticAct ever, so the model cannot loop). Each per-arm case
    // dispatches to the arm-local FromArguments factory (in PageActionTools.cs).
    // Unknown tool name (the model invented one or called the brain-only
    // ActSemanticAct arm) -> null; the transport surfaces a typed
    // SemanticActResolutionException. A factory failure (FromArguments
    // returned a FailureReason) also reads as null; the resolver's contract
    // is "concrete arm, or nothing", no per-failure diagnostics beyond what
    // the transport's exception carries.
    private static PageAction? ParseActionTool(string toolName, JsonElement args)
        => toolName switch
        {
            PageActionTools.Click.Name => PageActionTools.Click.FromArguments(args).Value,
            PageActionTools.Wait.Name => PageActionTools.Wait.FromArguments(args).Value,
            PageActionTools.WaitForSelector.Name => PageActionTools.WaitForSelector.FromArguments(args).Value,
            PageActionTools.WaitForNetworkIdle.Name => PageActionTools.WaitForNetworkIdle.FromArguments(args).Value,
            PageActionTools.ScrollToEnd.Name => PageActionTools.ScrollToEnd.FromArguments(args).Value,
            PageActionTools.ScrollIntoView.Name => PageActionTools.ScrollIntoView.FromArguments(args).Value,
            PageActionTools.EvaluateExpression.Name => PageActionTools.EvaluateExpression.FromArguments(args).Value,
            PageActionTools.Press.Name => PageActionTools.Press.FromArguments(args).Value,
            PageActionTools.Fill.Name => PageActionTools.Fill.FromArguments(args).Value,
            _ => null,
        };

    private readonly record struct ResolveInput(string Intent, string Html);
}
