using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
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
/// missing required fields, malformed JSON) result in a <c>null</c> return —
/// the transport translates that to
/// <see cref="WebReaper.Core.Actions.Concrete.SemanticActResolutionException"/>.
/// The resolver never returns a <see cref="PageAction.SemanticAct"/> arm — it
/// would loop the transport's dispatch.
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

    private readonly IChatClient _chatClient;
    private readonly LlmActionResolverOptions _options;

    /// <summary>Construct with an <see cref="IChatClient"/> and optional
    /// <see cref="LlmActionResolverOptions"/>.</summary>
    public LlmActionResolver(IChatClient chatClient, LlmActionResolverOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _chatClient = chatClient;
        _options = options ?? new LlmActionResolverOptions();
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

        var userPrompt =
            "Intent: " + intent + "\n\n" +
            "Page (HTML, may be truncated):\n" + trimmedHtml;

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
        if (string.IsNullOrWhiteSpace(text)) return null;

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
        if (parsed is not JsonObject obj) return null;

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

    // Strip ```json ... ``` or ``` ... ``` if the model wrapped its JSON
    // despite the system instruction. Same defence as LlmContentExtractor.
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
