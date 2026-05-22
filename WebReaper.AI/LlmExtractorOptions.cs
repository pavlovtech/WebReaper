namespace WebReaper.AI;

/// <summary>
/// Knobs for <see cref="LlmContentExtractor"/> (ADR-0044). Defaults are
/// cheap and deterministic: Markdown pre-clean, 4096-token response cap,
/// temperature 0, no overridden system prompt.
/// </summary>
/// <param name="Model">The model id passed to
/// <c>ChatOptions.ModelId</c>. <c>null</c> means the chat client's
/// default — most consumers configure the model at the
/// <c>IChatClient</c> level and leave this null.</param>
/// <param name="UseMarkdownPreClean">Run the document through the
/// deterministic <c>MarkdownContentExtractor</c> (ADR-0040) before
/// sending to the LLM. Default <c>true</c> — typical ~10× token savings
/// over raw HTML on editorial pages. Opt-out for sites where chrome
/// stripping risks losing data the heuristic mis-reads as
/// navigation.</param>
/// <param name="MaxTokens">Response token cap
/// (<c>ChatOptions.MaxOutputTokens</c>). Default 4096 — a comfortable
/// ceiling for typical structured-record sizes.</param>
/// <param name="Temperature">Sampling temperature. Default 0 — extraction
/// is a deterministic task; non-zero is opt-in for sites where the
/// schema is fuzzy.</param>
/// <param name="SystemPrompt">Override the default extraction system
/// prompt. <c>null</c> uses the built-in prompt; supply a string to
/// override entirely.</param>
public sealed record LlmExtractorOptions(
    string? Model = null,
    bool UseMarkdownPreClean = true,
    int MaxTokens = 4096,
    float Temperature = 0.0f,
    string? SystemPrompt = null);
