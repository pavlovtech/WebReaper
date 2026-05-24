using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using WebReaper.AI.Llm;
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
/// Asked once per intent string per crawl: the model returns a JSON object
/// naming one of the four supported action shapes (<c>click</c>,
/// <c>waitFor</c>, <c>wait</c>, <c>evaluate</c>). The resolver constructs the
/// matching <see cref="PageAction"/> arm; the Puppeteer transport caches it
/// and dispatches the cached arm on every subsequent same-intent invocation
/// (the LLM-as-proposer / deterministic-as-decider pattern, ADR-0046 /
/// ADR-0047 generalised to actions).
/// </para>
/// <para>
/// Unsupported JSON shapes (a <c>kind</c> the prompt doesn't whitelist,
/// missing required fields, malformed JSON after retry) result in a <c>null</c>
/// return — the transport translates that to
/// <see cref="WebReaper.Core.Actions.Concrete.SemanticActResolutionException"/>.
/// The resolver never returns a <see cref="PageAction.SemanticAct"/> arm — it
/// would loop the transport's dispatch.
/// </para>
/// <para>
/// Internally delegates to <see cref="LlmCall{TResponse}"/> (ADR-0059) — the
/// fence-stripping, the bounded parse-retry, and
/// <see cref="ChatResponse.Usage"/> capture all live there.
/// </para>
/// </summary>
public sealed class LlmActionResolver : IActionResolver
{
    private const string DefaultSystemPrompt =
        "You are resolving a user's natural-language intent to a concrete " +
        "browser action on the supplied HTML page. " +
        "Return a single JSON object with one of these exact shapes:\n" +
        "  { \"kind\": \"click\",    \"selector\": \"<css>\" }\n" +
        "  { \"kind\": \"waitFor\",  \"selector\": \"<css>\", \"timeoutMs\": <int> }\n" +
        "  { \"kind\": \"wait\",     \"ms\":       <int>   }\n" +
        "  { \"kind\": \"evaluate\", \"expression\": \"<js>\" }\n" +
        "Pick the simplest action that satisfies the intent. Prefer a CSS " +
        "selector specific enough not to collide with other elements " +
        "(prefer id over class, class over tag; combine if needed). " +
        "Return JSON only — no commentary, no Markdown code fences. " +
        "If the intent cannot be satisfied by any of these shapes, return " +
        "an empty object: {}.";

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
            ParseResponse = ParseArmFromElement,
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
            // Adapter policy: parse-after-retry failure → null (the
            // transport translates that to SemanticActResolutionException).
            return null;
        }
    }

    private static string BuildUserPrompt(ResolveInput input) =>
        "Intent: " + input.Intent + "\n\n" +
        "Page (HTML, may be truncated):\n" + input.Html;

    private static PageAction? ParseArmFromElement(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            // A non-object response is "unsupported shape" — return null
            // (not throw). The descriptor's ParseResponse contract:
            // throwing forces a retry; returning the default(TResponse)
            // is a clean "model said nothing actionable."
            return null;
        }

        // Round-trip into a JsonObject so the per-arm parsing matches the
        // existing shape and keeps the whitelist authority in one place.
        var node = JsonNode.Parse(element.GetRawText());
        if (node is not JsonObject obj) return null;

        return ParseArm(obj);
    }

    // The whitelist of arm shapes. Any other "kind" -> null (the transport
    // surfaces a typed SemanticActResolutionException). SemanticAct
    // deliberately not in this list — the resolver must never return one
    // (would loop the transport).
    private static PageAction? ParseArm(JsonObject obj)
    {
        var kind = obj["kind"]?.GetValue<string>();
        return kind switch
        {
            "click"    when obj["selector"]?.GetValue<string>() is { Length: > 0 } sel
                                  => new PageAction.Click(sel),
            "waitFor"  when obj["selector"]?.GetValue<string>() is { Length: > 0 } sel
                                  => new PageAction.WaitForSelector(sel, GetIntOrDefault(obj, "timeoutMs", 30_000)),
            "wait"                => new PageAction.Wait(GetIntOrDefault(obj, "ms", 0)),
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

    private readonly record struct ResolveInput(string Intent, string Html);
}
