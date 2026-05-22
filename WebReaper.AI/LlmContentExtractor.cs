using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Domain.Parsing;

namespace WebReaper.AI;

/// <summary>
/// The LLM adapter of the <see cref="IContentExtractor"/> seam
/// (ADR-0044) — the third adapter (after the deterministic
/// <c>SchemaFold</c> and the no-schema <c>MarkdownContentExtractor</c>).
/// Bound to <c>Microsoft.Extensions.AI.Abstractions</c>'s
/// <see cref="IChatClient"/> — the consumer brings their own concrete
/// chat client (OpenAI, Anthropic via wrapper, Ollama, anything
/// implementing the interface).
/// <para>
/// The extractor pre-cleans the document to Markdown by default
/// (ADR-0040's extractor — ~10× token savings vs raw HTML), composes a
/// system+user message pair, sets JSON response format, and parses
/// the model's text response as <see cref="JsonObject"/>. The schema
/// is required (it's the structured-output spec); selectors in the
/// schema are dropped — the LLM extracts semantically, not by
/// selector.
/// </para>
/// </summary>
public sealed class LlmContentExtractor : IContentExtractor
{
    private const string DefaultSystemPrompt =
        "You are extracting structured data from a web page. " +
        "Match the provided JSON schema exactly. " +
        "Output only valid JSON conforming to the schema — no commentary, " +
        "no Markdown code fences, no surrounding text. " +
        "If a field cannot be found, output an empty string for it (or " +
        "an empty array for list fields). Preserve the structure.";

    private readonly IChatClient _chatClient;
    private readonly LlmExtractorOptions _options;
    private readonly MarkdownContentExtractor _markdown = new();

    /// <summary>Construct with an <see cref="IChatClient"/> and
    /// optional options (defaults: Markdown pre-clean, 4096-token
    /// response cap, temperature 0).</summary>
    public LlmContentExtractor(IChatClient chatClient, LlmExtractorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _chatClient = chatClient;
        _options = options ?? new LlmExtractorOptions();
    }

    /// <inheritdoc/>
    public async Task<JsonObject> ExtractAsync(string document, Schema? schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var content = _options.UseMarkdownPreClean
            ? await PreCleanToMarkdownAsync(document)
            : document;

        var jsonSchema = SchemaJsonSchemaBridge.ToJsonSchema(schema);

        var systemPrompt = _options.SystemPrompt ?? DefaultSystemPrompt;
        var userPrompt =
            "JSON Schema:\n" + jsonSchema.ToJsonString() + "\n\n" +
            "Page content:\n" + content + "\n\n" +
            "Extract.";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userPrompt)
        };

        var chatOptions = new ChatOptions
        {
            ModelId = _options.Model,
            Temperature = _options.Temperature,
            MaxOutputTokens = _options.MaxTokens,
            // JSON response format. The chat client transparently
            // upgrades to native JSON Schema mode if the underlying
            // model supports it (OpenAI's response_format, etc.).
            ResponseFormat = ChatResponseFormat.Json
        };

        var response = await _chatClient.GetResponseAsync(messages, chatOptions);

        var text = ExtractText(response);
        text = StripJsonFences(text);

        var parsed = JsonNode.Parse(text)
            ?? throw new InvalidOperationException(
                "LLM returned empty or null JSON response.");

        if (parsed is not JsonObject obj)
            throw new InvalidOperationException(
                $"LLM response was not a JSON object (was {parsed.GetType().Name}).");

        return obj;
    }

    private async Task<string> PreCleanToMarkdownAsync(string document)
    {
        // MarkdownContentExtractor returns {title, markdown} — we want
        // just the Markdown body for the prompt.
        var markdownResult = await _markdown.ExtractAsync(document, schema: null);
        var md = markdownResult["markdown"]?.GetValue<string>() ?? string.Empty;
        var title = markdownResult["title"]?.GetValue<string>();

        // Prepend the title as an H1 so the model sees it in-context
        // even when the heuristic moved it from the document body.
        return string.IsNullOrEmpty(title) ? md : $"# {title}\n\n{md}";
    }

    private static string ExtractText(ChatResponse response)
    {
        // Modern Microsoft.Extensions.AI surfaces text via the
        // ChatResponse.Text helper (shorthand for the concatenated
        // text contents of the last assistant message).
        var text = response.Text;
        return text ?? string.Empty;
    }

    // Strip ```json ... ``` or ``` ... ``` if the model wrapped its
    // JSON despite being told not to (cheap defence; some smaller
    // models ignore the system instruction).
    private static string StripJsonFences(string text)
    {
        var trimmed = text.Trim();

        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;

        // Drop the opening fence + optional language tag.
        var newlineIdx = trimmed.IndexOf('\n');
        if (newlineIdx < 0) return trimmed;
        var body = trimmed[(newlineIdx + 1)..];

        // Drop the trailing fence.
        var endIdx = body.LastIndexOf("```", StringComparison.Ordinal);
        if (endIdx > 0) body = body[..endIdx];

        return body.Trim();
    }
}
