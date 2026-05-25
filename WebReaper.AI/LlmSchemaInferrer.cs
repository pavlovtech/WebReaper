using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using WebReaper.AI.Llm;
using WebReaper.Core.Markdown;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Domain.Parsing;

namespace WebReaper.AI;

/// <summary>
/// The default <see cref="ISchemaInferrer"/> (ADR-0067) — the fifth
/// <c>Llm*</c> adapter, sibling to <see cref="LlmContentExtractor"/>,
/// <see cref="LlmSelectorRepairer"/>, <see cref="LlmActionResolver"/>, and
/// <see cref="LlmAgentBrain"/>. Asks the LLM for a flat field-name →
/// CSS-selector map from the page content (optionally biased by a
/// natural-language goal); converts the response to a
/// <see cref="Schema"/> the deterministic fold can apply.
/// <para>
/// One <see cref="LlmCall{TResponse}.InvokeAsync"/> per
/// <see cref="WebReaper.Core.Parser.Concrete.LearnedSchemaContentExtractor"/>
/// instance — the wrapper
/// caches the result for the rest of the crawl, so this adapter pays the
/// LLM exactly once per engine. The cheapest dock of the project-level
/// proposer-validator pattern.
/// </para>
/// <para>
/// Internally delegates to <see cref="LlmCall{TResponse}"/> (ADR-0059) —
/// fence-stripping, the bounded parse-retry, and
/// <see cref="ChatResponse.Usage"/> capture all live there. ADR-0065
/// caching applies (per-role <see cref="LlmSchemaInferrerOptions.CachePolicy"/>);
/// ADR-0066 telemetry attributes the call to <c>nameof(LlmSchemaInferrer)</c>.
/// </para>
/// </summary>
public sealed class LlmSchemaInferrer : ISchemaInferrer
{
    private const string DefaultSystemPrompt =
        "You are inferring a CSS-selector-based extraction schema for a web " +
        "scraper. Examine the page content and the (optional) goal; propose " +
        "JSON of the form { \"fields\": { \"name\": \"selector\", ... } } " +
        "where each entry maps an output field name to a CSS selector that " +
        "extracts that field's text from the page. " +
        "Prefer stable selectors: id > class > tag; combine when needed for " +
        "uniqueness. Use semantic field names (lowercase_with_underscores). " +
        "Output only the JSON object, no commentary, no Markdown code fences.";

    private readonly LlmCall<JsonObject> _call;
    private readonly LlmSchemaInferrerOptions _options;

    /// <summary>Construct with an <see cref="IChatClient"/> and optional
    /// <see cref="LlmSchemaInferrerOptions"/>.</summary>
    /// <param name="chatClient">The Microsoft.Extensions.AI chat client.</param>
    /// <param name="options">Optional <see cref="LlmSchemaInferrerOptions"/>;
    /// defaults applied when null.</param>
    /// <param name="telemetry">Optional <see cref="ILlmCallTelemetry"/>
    /// (ADR-0066). Threaded by <c>.WithLlmSchemaInferrer(...)</c> from
    /// the builder; à la carte construction defaults to the null
    /// implementation.</param>
    public LlmSchemaInferrer(
        IChatClient chatClient,
        LlmSchemaInferrerOptions? options = null,
        ILlmCallTelemetry? telemetry = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _options = options ?? new LlmSchemaInferrerOptions();
        _call = new LlmCall<JsonObject>(chatClient, new LlmCallDescriptor<JsonObject>
        {
            Name = nameof(LlmSchemaInferrer),
            SystemPrompt = _options.SystemPrompt ?? DefaultSystemPrompt,
            BuildUserMessage = input => BuildUserPrompt((InferInput)input),
            ParseResponse = ParseInferredSchema,
            Model = _options.Model,
            Temperature = _options.Temperature,
            MaxResponseTokens = _options.MaxResponseTokens,
            SystemPromptCache = _options.CachePolicy ?? CachePolicy.Default,
        }, telemetry: telemetry);
    }

    /// <inheritdoc/>
    public async Task<Schema> InferAsync(
        string document,
        string? goal = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        // ADR-0063: call the HtmlToMarkdown primitive directly instead of
        // routing through the MarkdownContentExtractor adapter. Pre-clean
        // is the typical ~10× token savings — opt-out via the option.
        var content = _options.UseMarkdownPreClean
            ? HtmlToMarkdown.Convert(document)
            : document;

        // Truncate to the per-call ceiling. The inferrer runs once per
        // crawl so per-page cost matters less than for the extractor, but
        // an unbounded HTML page can still blow the context window.
        var trimmed = content.Length > _options.MaxContentChars
            ? content[.._options.MaxContentChars]
            : content;

        var input = new InferInput(trimmed, goal);
        var result = await _call.InvokeAsync(input, cancellationToken).ConfigureAwait(false);
        return BuildFlatSchema(result.Value);
    }

    private static string BuildUserPrompt(InferInput input)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(input.Goal))
        {
            sb.Append("Goal: ").AppendLine(input.Goal);
            sb.AppendLine();
        }
        sb.AppendLine("Page:");
        sb.AppendLine(input.Content);
        sb.AppendLine();
        sb.AppendLine(
            "Propose the extraction schema as JSON of the form " +
            "{ \"fields\": { \"name\": \"css-selector\", ... } }.");
        return sb.ToString();
    }

    private static JsonObject ParseInferredSchema(JsonElement element)
    {
        // Two accepted shapes — the descriptor's prompt asks for
        // { "fields": { ... } } but a model that returns the bare field
        // map { "name": "selector", ... } is the second-most-likely
        // shape and worth honouring without a retry.
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Inferrer response was not a JSON object (kind={element.ValueKind}).");
        }

        if (element.TryGetProperty("fields", out var fields) &&
            fields.ValueKind == JsonValueKind.Object)
        {
            return ToJsonObject(fields);
        }

        return ToJsonObject(element);
    }

    private static JsonObject ToJsonObject(JsonElement element)
    {
        var node = JsonNode.Parse(element.GetRawText())
            ?? throw new InvalidOperationException("Inferrer response parsed to null.");
        if (node is not JsonObject obj)
        {
            throw new InvalidOperationException(
                $"Inferrer response was not a JSON object (was {node.GetType().Name}).");
        }
        return obj;
    }

    private static Schema BuildFlatSchema(JsonObject fields)
    {
        // ADR-0067 fork 5: single-level flat schemas only in v1 (matches
        // the ADR-0045 source-gen v1 constraint). The fields map is
        // { fieldName: cssSelector } — one SchemaElement per entry.
        var schema = new Schema();
        foreach (var (name, valueNode) in fields)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (valueNode is null) continue;

            // Accept both the flat string shape ("title": "h1") and the
            // structured shape ("title": { "selector": "h1" }) — the
            // latter shows up when a verbose model embellishes.
            string? selector = null;
            if (valueNode is JsonValue value && value.TryGetValue<string>(out var direct))
            {
                selector = direct;
            }
            else if (valueNode is JsonObject nested &&
                     nested.TryGetPropertyValue("selector", out var nestedSelector) &&
                     nestedSelector is JsonValue nestedValue &&
                     nestedValue.TryGetValue<string>(out var nestedString))
            {
                selector = nestedString;
            }

            if (string.IsNullOrWhiteSpace(selector)) continue;

            // ADR-0028 construction guards: Schema.Add throws on
            // empty Field / Selector — pre-filter above.
            schema.Add(new SchemaElement(name, selector));
        }

        if (schema.Children.Count == 0)
        {
            throw new InvalidOperationException(
                "Inferrer returned no usable field/selector pairs. The response " +
                "JSON parsed but contained no entries with a non-empty selector.");
        }

        return schema;
    }

    private readonly record struct InferInput(string Content, string? Goal);
}
