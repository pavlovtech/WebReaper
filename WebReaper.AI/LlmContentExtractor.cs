using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using WebReaper.AI.Llm;
using WebReaper.Core.Markdown;
using WebReaper.Core.Parser.Abstract;
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
/// <para>
/// Internally delegates to <see cref="LlmCall{TResponse}"/> (ADR-0059) —
/// fence-stripping, the bounded parse-retry, and
/// <see cref="ChatResponse.Usage"/> capture all live there.
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

    private readonly LlmCall<JsonObject> _call;
    private readonly LlmExtractorOptions _options;

    /// <summary>Construct with an <see cref="IChatClient"/> and
    /// optional options (defaults: Markdown pre-clean, 4096-token
    /// response cap, temperature 0).</summary>
    public LlmContentExtractor(IChatClient chatClient, LlmExtractorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _options = options ?? new LlmExtractorOptions();
        _call = new LlmCall<JsonObject>(chatClient, new LlmCallDescriptor<JsonObject>
        {
            Name = nameof(LlmContentExtractor),
            SystemPrompt = _options.SystemPrompt ?? DefaultSystemPrompt,
            BuildUserMessage = input => BuildUserMessage((ExtractInput)input),
            ParseResponse = ParseExtractedJson,
            Model = _options.Model,
            Temperature = _options.Temperature,
            MaxResponseTokens = _options.MaxTokens,
            SystemPromptCache = _options.CachePolicy ?? CachePolicy.Default,
        });
    }

    /// <inheritdoc/>
    public async Task<JsonObject> ExtractAsync(string document, Schema? schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var content = _options.UseMarkdownPreClean
            ? PreCleanToMarkdown(document)
            : document;

        var jsonSchema = SchemaJsonSchemaBridge.ToJsonSchema(schema);
        var input = new ExtractInput(content, jsonSchema);

        try
        {
            var result = await _call.InvokeAsync(input);
            return result.Value;
        }
        catch (LlmCallException ex)
        {
            throw new InvalidOperationException(
                "LLM extractor failed to parse a structured response: " + ex.Message, ex);
        }
    }

    private static string BuildUserMessage(ExtractInput input) =>
        "JSON Schema:\n" + input.JsonSchema.ToJsonString() + "\n\n" +
        "Page content:\n" + input.Content + "\n\n" +
        "Extract.";

    private static JsonObject ParseExtractedJson(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException(
                $"LLM response was not a JSON object (kind={element.ValueKind}).");

        // Round-trip through ToString for a fresh JsonObject — the
        // descriptor's JsonElement was cloned in LlmCall, so the
        // mutation-safe JsonObject is what callers expect.
        var node = JsonNode.Parse(element.GetRawText())
            ?? throw new InvalidOperationException("LLM response parsed to null.");
        if (node is not JsonObject obj)
            throw new InvalidOperationException(
                $"LLM response was not a JSON object (was {node.GetType().Name}).");
        return obj;
    }

    private static string PreCleanToMarkdown(string document)
    {
        // ADR-0063: call the HtmlToMarkdown primitive directly instead
        // of going through the MarkdownContentExtractor adapter. The
        // adapter would wrap the result in a JsonObject only for us to
        // pull the markdown string back out — eight tokens of friction
        // resolved by the primitive's two-overload shape.
        var content = HtmlToMarkdown.ExtractMainContent(document);

        // Prepend the title as an H1 so the model sees it in-context
        // even when the heuristic moved it from the document body.
        return string.IsNullOrEmpty(content.Title)
            ? content.Markdown
            : $"# {content.Title}\n\n{content.Markdown}";
    }

    private readonly record struct ExtractInput(string Content, JsonObject JsonSchema);
}
