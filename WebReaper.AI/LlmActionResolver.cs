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
/// nine concrete action tools (<c>ActClick</c>, <c>ActWait</c>,
/// <c>ActWaitForSelector</c>, <c>ActWaitForNetworkIdle</c>,
/// <c>ActScrollToEnd</c>, <c>ActEvaluate</c>, <c>ActScrollIntoView</c>,
/// <c>ActPress</c>, <c>ActFill</c>) per ADR-0060 / ADR-0074. The resolver
/// constructs the matching <see cref="PageAction"/> arm; the browser
/// transport caches it and dispatches the cached arm on every subsequent
/// same-intent invocation (the LLM-as-proposer / deterministic-as-decider
/// pattern, ADR-0046 / ADR-0047 generalised to actions).
/// </para>
/// <para>
/// The resolver's tool registry has nine arms; never <c>ActSemanticAct</c>
/// (fork 8 verdict: the closed sum is closed at the resolver's tool list,
/// structurally preventing the resolver from looping the transport's
/// resolution path). Post ADR-0078 Axis B the registry and the parse are
/// derived views of <see cref="WebReaper.AI.Tools.PageActionTools.Arms"/> —
/// SemanticAct exposes no resolver adapter, so it is absent from both. Unknown
/// tool name returns <c>null</c>; the transport translates that to
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
            ParseToolCall = AgentDecisionTools.ParseResolverTool,
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

    // ADR-0078 Axis B: the resolver's tool list and its parse are both derived
    // views of PageActionTools.Arms (the entries exposing a resolver adapter),
    // so ForResolver() and ParseResolverTool cannot drift. Fork 8 is structural:
    // SemanticAct exposes no resolver adapter, so the resolver neither offers
    // nor parses ActSemanticAct — the model cannot loop. An unknown tool name
    // (a hallucination, or the brain-only ActSemanticAct) or a per-arm factory
    // failure both read as null; the transport surfaces a typed
    // SemanticActResolutionException.
    private readonly record struct ResolveInput(string Intent, string Html);
}
