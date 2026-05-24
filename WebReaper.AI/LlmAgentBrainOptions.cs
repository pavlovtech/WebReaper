namespace WebReaper.AI;

/// <summary>
/// Knobs for <see cref="LlmAgentBrain"/> (ADR-0051). Defaults are cheap and
/// deterministic: temperature 0, a 1024-token response cap (the JSON object
/// is small but Extract schemas can be several fields), and the
/// brain's-view caps for history / visited / candidate URLs / page markdown
/// (fork 3 verdict — bounded state).
/// </summary>
/// <param name="Model">The model id passed to <c>ChatOptions.ModelId</c>.
/// <c>null</c> means the chat client's default — most consumers configure
/// the model at the <see cref="Microsoft.Extensions.AI.IChatClient"/> level
/// and leave this null.</param>
/// <param name="Temperature">Sampling temperature. Default 0 — page
/// selection is a deterministic task, the same state should resolve to the
/// same decision.</param>
/// <param name="MaxResponseTokens">Response token cap
/// (<c>ChatOptions.MaxOutputTokens</c>). Default 1024 — a small JSON object
/// naming the decision arm, with room for a multi-field Extract schema.</param>
/// <param name="SystemPrompt">Override the default system prompt. <c>null</c>
/// uses the built-in prompt; supply a string to override entirely.</param>
public sealed record LlmAgentBrainOptions(
    string? Model = null,
    float Temperature = 0.0f,
    int MaxResponseTokens = 1024,
    string? SystemPrompt = null);
