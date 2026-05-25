using WebReaper.AI.Llm;

namespace WebReaper.AI;

/// <summary>
/// Knobs for <see cref="LlmSchemaInferrer"/> (ADR-0067). Defaults are cheap
/// and deterministic: Markdown pre-clean, 32 000-char content cap, 1024-token
/// response cap (the inferred schema is a small JSON object — one entry per
/// field), temperature 0, no overridden system prompt, no cache-policy
/// override.
/// </summary>
/// <param name="Model">The model id passed to <c>ChatOptions.ModelId</c>.
/// <c>null</c> means the chat client's default — most consumers configure
/// the model at the <c>IChatClient</c> level and leave this null.</param>
/// <param name="UseMarkdownPreClean">Run the document through the
/// deterministic <see cref="WebReaper.Core.Markdown.HtmlToMarkdown.Convert"/>
/// primitive (ADR-0063) before sending to the LLM. Default <c>true</c> —
/// typical ~10× token savings over raw HTML on editorial pages. Opt-out for
/// pages where the structural chrome (e.g. attribute-rich tables) carries
/// the signal the inferrer needs to write selectors against.</param>
/// <param name="MaxContentChars">Truncate the (Markdown-cleaned or raw)
/// document to this many characters before sending to the LLM. Default
/// 32 000 — enough for the typical product / job-listing / article page to
/// fit, well under common context windows. The inferrer is one-shot per
/// crawl, so the per-page cost matters less than for the extractor.</param>
/// <param name="MaxResponseTokens">Response token cap
/// (<c>ChatOptions.MaxOutputTokens</c>). Default 1024 — the inferred
/// schema is a small JSON object naming each field's CSS selector;
/// 1024 tokens accommodates ~30+ fields comfortably.</param>
/// <param name="Temperature">Sampling temperature. Default 0 — inference
/// is a deterministic task (the same page should yield the same schema).</param>
/// <param name="SystemPrompt">Override the default inference system
/// prompt. <c>null</c> uses the built-in prompt; supply a string to
/// override entirely.</param>
/// <param name="CachePolicy">Per-role system-prompt caching policy
/// (ADR-0065). <c>null</c> (the default) means <see cref="WebReaper.AI.Llm.CachePolicy.Default"/>
/// at the descriptor — no provider-specific hint added. Set explicitly to
/// <see cref="WebReaper.AI.Llm.CachePolicy.Hinted"/> to enable
/// provider-cache hints. (Single-page inference is one-shot per crawl —
/// the cache-write premium typically does not amortise; <c>Default</c> is
/// the right starting policy.)</param>
public sealed record LlmSchemaInferrerOptions(
    string? Model = null,
    bool UseMarkdownPreClean = true,
    int MaxContentChars = 32_000,
    int MaxResponseTokens = 1024,
    float Temperature = 0.0f,
    string? SystemPrompt = null,
    CachePolicy? CachePolicy = null);
