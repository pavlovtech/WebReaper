namespace WebReaper.AI.Llm;

/// <summary>
/// Thrown by <see cref="LlmCall{TResponse}.InvokeAsync"/> when the bounded
/// parse-retry exhausts without producing a parseable response (or, in
/// tool-call mode, when the model never emits a <c>FunctionCallContent</c>).
/// Carries the raw response + the attempt count + the descriptor name
/// so callers can log enough context to debug without re-running the call.
/// <para>
/// Each per-role adapter decides how to translate this — <c>LlmContentExtractor</c>
/// re-throws as <see cref="InvalidOperationException"/>; <c>LlmActionResolver</c>
/// catches and returns <c>null</c>; <c>LlmAgentBrain</c> catches and returns
/// <c>AgentDecision.Stop</c>; <c>LlmSelectorRepairer</c> catches and returns
/// <c>null</c>.
/// </para>
/// </summary>
public sealed class LlmCallException : Exception
{
    /// <summary>The raw model response that failed to parse (or
    /// <see cref="string.Empty"/> in tool-call mode if the response
    /// contained no <c>FunctionCallContent</c>).</summary>
    public string RawResponse { get; }

    /// <summary>How many invocations the mechanism made before giving up
    /// (1 or 2 — first call + optional retry).</summary>
    public int Attempts { get; }

    /// <summary>The descriptor's <c>Name</c> field, for diagnostic
    /// breadcrumbs in logs.</summary>
    public string DescriptorName { get; }

    /// <summary>Construct an <see cref="LlmCallException"/>.</summary>
    public LlmCallException(
        string message,
        string rawResponse,
        int attempts,
        string descriptorName,
        Exception? innerException = null)
        : base(message, innerException)
    {
        RawResponse = rawResponse;
        Attempts = attempts;
        DescriptorName = descriptorName;
    }
}
