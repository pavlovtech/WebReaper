using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using WebReaper.AI.Llm;
using WebReaper.Core.Markdown;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Domain.Parsing;

namespace WebReaper.AI;

/// <summary>
/// The schema-free LLM <see cref="IContentExtractor"/> strategy (ADR-0084):
/// the "Prompt extraction" adapter behind the CLI's <c>--prompt</c>. The LLM
/// reads each page and extracts per a natural-language instruction, returning
/// whatever JSON shape fits. The schema-free sibling of
/// <see cref="LlmContentExtractor"/>: same <see cref="LlmCall{TResponse}"/>
/// mechanism and Markdown pre-clean, but a free-text instruction replaces the
/// <see cref="Schema"/> (no <c>SchemaJsonSchemaBridge</c> step), so the
/// <see cref="ExtractAsync"/> schema argument is ignored and may be null.
/// <para>
/// One LLM call per page. Robust on structurally heterogeneous pages, with
/// cost scaling by page count: the deliberate contrast to
/// <see cref="WebReaper.Core.Parser.Concrete.LearnedSchemaContentExtractor"/>'s
/// infer-once-then-deterministic-fold path (<c>--infer</c>).
/// </para>
/// </summary>
public sealed class PromptContentExtractor : IContentExtractor
{
    private const string DefaultSystemPrompt =
        "You extract structured data from a web page according to the user's instruction. " +
        "Output only valid JSON that captures what the instruction asks for: no commentary, " +
        "no Markdown code fences, no surrounding text. Choose concise field names. When the " +
        "instruction implies a list, return a JSON array under a sensibly named field.";

    private readonly LlmCall<JsonObject> _call;
    private readonly LlmExtractorOptions _options;
    private readonly string _instruction;

    /// <summary>Construct with an <see cref="IChatClient"/>, the
    /// natural-language extraction instruction, and optional options.</summary>
    /// <param name="chatClient">The Microsoft.Extensions.AI chat client.</param>
    /// <param name="instruction">The natural-language instruction describing
    /// what to extract (e.g. "all C-level executives with name, title, email").</param>
    /// <param name="options">Optional <see cref="LlmExtractorOptions"/>;
    /// defaults applied when null.</param>
    /// <param name="telemetry">Optional <see cref="ILlmCallTelemetry"/> (ADR-0066).</param>
    public PromptContentExtractor(
        IChatClient chatClient,
        string instruction,
        LlmExtractorOptions? options = null,
        ILlmCallTelemetry? telemetry = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);
        _instruction = instruction;
        _options = options ?? new LlmExtractorOptions();
        _call = new LlmCall<JsonObject>(chatClient, new LlmCallDescriptor<JsonObject>
        {
            Name = nameof(PromptContentExtractor),
            SystemPrompt = _options.SystemPrompt ?? DefaultSystemPrompt,
            BuildUserMessage = input => BuildUserMessage((string)input),
            ParseResponse = ParseExtractedJson,
            Model = _options.Model,
            Temperature = _options.Temperature,
            MaxResponseTokens = _options.MaxTokens,
            SystemPromptCache = _options.CachePolicy ?? CachePolicy.Default,
        }, telemetry: telemetry);
    }

    /// <inheritdoc/>
    /// <remarks>ADR-0084: schema-free. The <paramref name="schema"/> argument
    /// is ignored (the instruction is the spec); a null schema is valid here,
    /// unlike the deterministic fold.</remarks>
    public async Task<JsonObject> ExtractAsync(string document, Schema? schema)
    {
        var content = _options.UseMarkdownPreClean ? PreCleanToMarkdown(document) : document;

        try
        {
            var result = await _call.InvokeAsync(content);
            return result.Value;
        }
        catch (LlmCallException ex)
        {
            throw new InvalidOperationException(
                "LLM prompt extractor failed to parse a structured response: " + ex.Message, ex);
        }
    }

    private string BuildUserMessage(string content) =>
        "Instruction:\n" + _instruction + "\n\nPage content:\n" + content + "\n\nReturn JSON.";

    // Schema-free responses may legitimately be a top-level array or scalar;
    // ParsedData carries a JsonObject, so wrap non-object roots rather than
    // throwing (the contrast to LlmContentExtractor, which requires an object).
    private static JsonObject ParseExtractedJson(JsonElement element)
    {
        var node = JsonNode.Parse(element.GetRawText())
            ?? throw new InvalidOperationException("LLM response parsed to null.");
        return node switch
        {
            JsonObject obj => obj,
            JsonArray array => new JsonObject { ["items"] = array },
            _ => new JsonObject { ["value"] = node },
        };
    }

    private static string PreCleanToMarkdown(string document)
    {
        var content = HtmlToMarkdown.ExtractMainContent(document);
        return string.IsNullOrEmpty(content.Title)
            ? content.Markdown
            : $"# {content.Title}\n\n{content.Markdown}";
    }
}
