using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using WebReaper.AI.Llm;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Domain.Parsing;

namespace WebReaper.AI;

/// <summary>
/// The default <see cref="ISelectorRepairer"/> (ADR-0047): asks the
/// LLM for patched selectors when the deterministic fold's output
/// fails validation, returns a Schema with the selectors swapped.
/// Internally delegates to <see cref="LlmCall{TResponse}"/> (ADR-0059)
/// — the fence-stripping, the bounded parse-retry, and
/// <see cref="ChatResponse.Usage"/> capture all live there.
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

    private readonly LlmCall<JsonObject?> _call;
    private readonly LlmExtractorOptions _options;
    private readonly MarkdownContentExtractor _markdown = new();

    /// <summary>Construct with an <see cref="IChatClient"/> and optional
    /// <see cref="LlmExtractorOptions"/>.</summary>
    public LlmSelectorRepairer(IChatClient chatClient, LlmExtractorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _options = options ?? new LlmExtractorOptions();
        _call = new LlmCall<JsonObject?>(chatClient, new LlmCallDescriptor<JsonObject?>
        {
            Name = nameof(LlmSelectorRepairer),
            SystemPrompt = _options.SystemPrompt ?? DefaultSystemPrompt,
            BuildUserMessage = input => BuildUserPrompt((RepairInput)input),
            ParseResponse = ParseSelectorMap,
            Model = _options.Model,
            Temperature = _options.Temperature,
            MaxResponseTokens = _options.MaxTokens,
        });
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

        var originalSelectors = CollectSelectors(original);
        var input = new RepairInput(originalSelectors, failedResult, pageContent);

        JsonObject? selectorMap;
        try
        {
            var result = await _call.InvokeAsync(input, cancellationToken);
            selectorMap = result.Value;
        }
        catch (LlmCallException)
        {
            // Adapter policy: parse-after-retry failure → null. The
            // SelfHealingContentExtractor falls back to the original
            // deterministic output.
            return null;
        }

        if (selectorMap is null || selectorMap.Count == 0) return null;

        return ApplySelectors(original, selectorMap);
    }

    private static string BuildUserPrompt(RepairInput input)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Original selectors (field → selector):");
        foreach (var (field, selector) in input.OriginalSelectors)
            sb.AppendLine($"  {field} → {selector}");
        sb.AppendLine();
        sb.AppendLine("Failed result:");
        sb.AppendLine(input.FailedResult.ToJsonString());
        sb.AppendLine();
        sb.AppendLine("Page:");
        sb.AppendLine(input.PageContent);
        sb.AppendLine();
        sb.AppendLine("Propose new selectors as JSON (field name → selector string).");
        return sb.ToString();
    }

    private static JsonObject? ParseSelectorMap(JsonElement element)
    {
        // A non-object response is "no repairs proposed" — return null
        // (not throw). Throwing would force a retry, but the empty
        // shape is a valid model response meaning "I don't know."
        if (element.ValueKind != JsonValueKind.Object) return null;

        var node = JsonNode.Parse(element.GetRawText());
        return node as JsonObject;
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

    private readonly record struct RepairInput(
        List<(string Field, string Selector)> OriginalSelectors,
        JsonObject FailedResult,
        string PageContent);
}
