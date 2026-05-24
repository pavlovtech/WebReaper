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
/// The resolver's tool registry has six arms — never <c>ActSemanticAct</c>
/// (fork 8 verdict — the closed sum is closed at the resolver's tool list,
/// structurally preventing the resolver from looping the transport's
/// resolution path). Unknown tool name -> <c>null</c>; the transport
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
    private const string DefaultSystemPrompt =
        "You are resolving a user's natural-language intent to a concrete " +
        "browser action on the supplied HTML page. Call EXACTLY ONE of the " +
        "provided action tools to indicate the concrete action. Pick the " +
        "simplest action that satisfies the intent. Prefer a CSS selector " +
        "specific enough not to collide with other elements (prefer id over " +
        "class, class over tag; combine if needed).";

    private readonly LlmCall<PageAction?> _call;
    private readonly LlmActionResolverOptions _options;

    /// <summary>Construct with an <see cref="IChatClient"/> and optional
    /// <see cref="LlmActionResolverOptions"/>.</summary>
    public LlmActionResolver(IChatClient chatClient, LlmActionResolverOptions? options = null)
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
        });
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

    // ADR-0060 fork 8: the resolver's tool list has six arms; the closed
    // sum is closed structurally (no ActSemanticAct ever, so the model
    // cannot loop). Unknown tool name (the model invented one or called
    // a brain-only arm) -> null; the transport surfaces a typed
    // SemanticActResolutionException.
    private static PageAction? ParseActionTool(string toolName, JsonElement args)
        => toolName switch
        {
            "ActClick"
                when TryGetString(args, "selector") is { Length: > 0 } sel
                => new PageAction.Click(sel),

            "ActWait"
                => new PageAction.Wait(TryGetInt(args, "ms") ?? 0),

            "ActWaitForSelector"
                when TryGetString(args, "selector") is { Length: > 0 } sel
                => new PageAction.WaitForSelector(sel, TryGetInt(args, "timeoutMs") ?? 30_000),

            "ActWaitForNetworkIdle"
                => new PageAction.WaitForNetworkIdle(),

            "ActScrollToEnd"
                => new PageAction.ScrollToEnd(),

            "ActEvaluate"
                when TryGetString(args, "expression") is { Length: > 0 } expr
                => new PageAction.EvaluateExpression(expr),

            _ => null,
        };

    private static string? TryGetString(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object) return null;
        if (!args.TryGetProperty(name, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Null => null,
            _ => null,
        };
    }

    private static int? TryGetInt(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object) return null;
        if (!args.TryGetProperty(name, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(el.GetString(), out var i) => i,
            _ => null,
        };
    }

    private readonly record struct ResolveInput(string Intent, string Html);
}
