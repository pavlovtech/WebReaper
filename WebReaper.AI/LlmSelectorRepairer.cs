using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Domain.Parsing;

namespace WebReaper.AI;

/// <summary>
/// The default <see cref="ISelectorRepairer"/> (ADR-0047): asks the
/// LLM for patched selectors when the deterministic fold's output
/// fails validation, returns a Schema with the selectors swapped.
/// </summary>
public sealed class LlmSelectorRepairer : ISelectorRepairer
{
    private const string DefaultSystemPrompt =
        "You are repairing CSS selectors for a web scraper. " +
        "The original selectors no longer match the page; some fields " +
        "have empty values in the failed extraction result. Given the page " +
        "content and the failed result, propose new selectors for the " +
        "fields with empty values. " +
        "Output a JSON object mapping field names to CSS selector strings. " +
        "Only include fields you can repair; omit fields where you cannot " +
        "find a working selector. Output only the JSON, no commentary.";

    private readonly IChatClient _chatClient;
    private readonly LlmExtractorOptions _options;
    private readonly MarkdownContentExtractor _markdown = new();

    public LlmSelectorRepairer(IChatClient chatClient, LlmExtractorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _chatClient = chatClient;
        _options = options ?? new LlmExtractorOptions();
    }

    /// <inheritdoc/>
    public async Task<Schema?> RepairAsync(
        Schema original,
        string document,
        JsonObject failedResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(failedResult);

        // Pre-clean to Markdown by default — same cost discipline as
        // the LLM extractor.
        var pageContent = _options.UseMarkdownPreClean
            ? await PreCleanToMarkdownAsync(document)
            : document;

        // Build the user prompt: original selectors + failed result + page.
        var originalSelectors = CollectSelectors(original);
        var sb = new StringBuilder();
        sb.AppendLine("Original selectors (field → selector):");
        foreach (var (field, selector) in originalSelectors)
            sb.AppendLine($"  {field} → {selector}");
        sb.AppendLine();
        sb.AppendLine("Failed result:");
        sb.AppendLine(failedResult.ToJsonString());
        sb.AppendLine();
        sb.AppendLine("Page:");
        sb.AppendLine(pageContent);
        sb.AppendLine();
        sb.AppendLine("Propose new selectors as JSON (field name → selector string).");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, _options.SystemPrompt ?? DefaultSystemPrompt),
            new(ChatRole.User, sb.ToString())
        };

        var response = await _chatClient.GetResponseAsync(messages, new ChatOptions
        {
            ModelId = _options.Model,
            Temperature = _options.Temperature,
            MaxOutputTokens = _options.MaxTokens,
            ResponseFormat = ChatResponseFormat.Json
        }, cancellationToken);

        var text = response.Text ?? string.Empty;
        text = StripJsonFences(text);

        JsonObject? selectorMap;
        try
        {
            selectorMap = JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }

        if (selectorMap is null || selectorMap.Count == 0) return null;

        return ApplySelectors(original, selectorMap);
    }

    private async Task<string> PreCleanToMarkdownAsync(string document)
    {
        var md = await _markdown.ExtractAsync(document, schema: null);
        return md["markdown"]?.GetValue<string>() ?? string.Empty;
    }

    private static List<(string Field, string Selector)> CollectSelectors(Schema schema)
    {
        var list = new List<(string, string)>();
        foreach (var element in schema.Children)
        {
            if (element.Field is null) continue;
            if (element is Schema container)
            {
                if (container.IsList && !string.IsNullOrEmpty(container.Selector))
                    list.Add((element.Field, container.Selector!));
                foreach (var (childField, childSelector) in CollectSelectors(container))
                    list.Add(($"{element.Field}.{childField}", childSelector));
            }
            else if (!string.IsNullOrEmpty(element.Selector))
            {
                list.Add((element.Field, element.Selector!));
            }
        }
        return list;
    }

    private static Schema ApplySelectors(Schema original, JsonObject newSelectors)
    {
        // Recursive copy with selector overrides. Honour the
        // ADR-0028 construction guards by going through Schema.Add /
        // SchemaElement construction.
        var copy = new Schema();
        foreach (var child in original.Children)
        {
            copy.Add(ApplyElement(child, newSelectors, prefix: string.Empty));
        }
        return copy;
    }

    private static SchemaElement ApplyElement(SchemaElement element, JsonObject newSelectors, string prefix)
    {
        var qualified = string.IsNullOrEmpty(prefix) ? element.Field! : $"{prefix}.{element.Field}";

        if (element is Schema container)
        {
            // For containers, attempt to find a replacement for the
            // container's own selector (only meaningful when IsList).
            string? newContainerSelector = null;
            if (container.IsList && newSelectors.TryGetPropertyValue(qualified, out var node))
                newContainerSelector = node?.GetValue<string>();

            var newContainer = container.IsList && !string.IsNullOrEmpty(newContainerSelector)
                ? Schema.ListOf(container.Field!, newContainerSelector!)
                : new Schema(container.Field!)
                {
                    Selector = container.IsList ? container.Selector ?? string.Empty : string.Empty,
                    IsList = container.IsList
                };

            foreach (var sub in container.Children)
                newContainer.Add(ApplyElement(sub, newSelectors, qualified));

            return newContainer;
        }

        // Leaf — check if we have a new selector.
        var selector = element.Selector ?? string.Empty;
        if (newSelectors.TryGetPropertyValue(qualified, out var newNode) && newNode is not null)
        {
            var maybeNew = newNode.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(maybeNew)) selector = maybeNew;
        }

        return new SchemaElement(element.Field!, selector)
        {
            Type = element.Type,
            IsList = element.IsList,
            Attr = element.Attr
        };
    }

    private static string StripJsonFences(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;
        var newlineIdx = trimmed.IndexOf('\n');
        if (newlineIdx < 0) return trimmed;
        var body = trimmed.Substring(newlineIdx + 1);
        var endIdx = body.LastIndexOf("```", StringComparison.Ordinal);
        if (endIdx > 0) body = body.Substring(0, endIdx);
        return body.Trim();
    }
}
