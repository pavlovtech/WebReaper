namespace WebReaper.AI.Tools;

/// <summary>
/// The result of parsing a tool-call's <c>FunctionCallContent.Arguments</c>
/// into a typed closed-sum arm — either the successfully-constructed arm
/// (<see cref="Value"/> non-null, <see cref="FailureReason"/> null) or a
/// human-readable description of why construction failed
/// (<see cref="Value"/> null, <see cref="FailureReason"/> the reason).
/// <para>
/// The two-shape return exists because the brain and the resolver wrap the
/// same failure differently: the brain converts a failure into
/// <see cref="WebReaper.Domain.Agent.AgentDecision.Stop"/> with the reason
/// baked into the audit-trail <c>Reason</c> string ("brain ActClick missing
/// 'selector': &lt;brain reason&gt;"); the resolver returns <c>null</c> so
/// the transport can surface a typed
/// <see cref="WebReaper.Core.Actions.Concrete.SemanticActResolutionException"/>.
/// Carrying both pieces lets each consumer apply its own policy.
/// </para>
/// <para>
/// <see cref="FailureReason"/> is a freeform string — typically "missing
/// '&lt;field&gt;'" for an absent required argument, or a structural
/// description ("schema was empty") for a parsed-but-invalid arm. The brain
/// composes the string into its Stop reason verbatim, so per-arm
/// <c>FromArguments</c> factories own their wording.
/// </para>
/// <para>
/// Sibling to <see cref="Llm.LlmCallResult{T}"/> on the same "named result
/// shape, not a tuple" axis. Public so consumer-authored tool-calling
/// adapters reuse the type instead of inventing their own.
/// </para>
/// </summary>
/// <typeparam name="T">The arm type the FromArguments factory constructs.</typeparam>
/// <param name="Value">The constructed arm on success; <c>null</c> on failure.</param>
/// <param name="FailureReason">A human-readable description of why
/// construction failed; <c>null</c> on success.</param>
public readonly record struct ToolCallResult<T>(T? Value, string? FailureReason) where T : class
{
    /// <summary>Construct a success result carrying the arm.</summary>
    public static ToolCallResult<T> Ok(T value) => new(value, null);

    /// <summary>Construct a failure result with a human-readable reason.</summary>
    public static ToolCallResult<T> Failed(string reason) => new(null, reason);
}
