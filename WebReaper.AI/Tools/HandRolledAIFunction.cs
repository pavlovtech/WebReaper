using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace WebReaper.AI.Tools;

/// <summary>
/// A minimal, AOT-friendly <see cref="AIFunction"/> subclass that carries a
/// pre-built parameter schema (<see cref="JsonObject"/>) and a name +
/// description (ADR-0060). The brain + resolver tool registries assemble one
/// per arm of their respective closed sums.
/// <para>
/// Hand-rolled — no reflection over .NET methods, no
/// <c>AIFunctionFactory.Create(MethodInfo)</c>. The schema is built as a
/// <see cref="JsonObject"/> literal in the per-arm factory; this class owns
/// the M.E.AI surface shape (the <see cref="JsonSchema"/> property must
/// return a <see cref="JsonElement"/>) and the no-op invocation contract
/// (the mechanism only puts the function on
/// <see cref="ChatOptions.Tools"/>; the SDK never calls
/// <see cref="InvokeCoreAsync"/> — that's the *consumer-side* execution
/// path, which doesn't apply to closed-sum tool dispatch).
/// </para>
/// </summary>
internal sealed class HandRolledAIFunction : AIFunction
{
    private readonly JsonElement _schema;

    /// <summary>The tool name surfaced to the model.</summary>
    public override string Name { get; }

    /// <summary>The tool description surfaced to the model.</summary>
    public override string Description { get; }

    /// <summary>The pre-built JSON Schema (M.E.AI surface returns
    /// <see cref="JsonElement"/>; we clone from the supplied
    /// <see cref="JsonObject"/> at construction so the live tree stays
    /// internal).</summary>
    public override JsonElement JsonSchema => _schema;

    public HandRolledAIFunction(string name, string description, JsonObject parametersSchema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(parametersSchema);

        Name = name;
        Description = description;
        // Materialise to a JsonElement via the canonical round-trip — the
        // base class exposes a JsonElement, not a JsonNode.
        using var doc = JsonDocument.Parse(parametersSchema.ToJsonString());
        _schema = doc.RootElement.Clone();
    }

    /// <summary>Never called for tool-call dispatch — the M.E.AI surface
    /// invokes this only when the consumer hooks tool *execution* (the
    /// SDK's <c>FunctionInvocationFilter</c> flow). Closed-sum tool
    /// dispatch (ADR-0060) reads the arguments off the
    /// <see cref="FunctionCallContent"/> in the response and constructs
    /// the domain arm in <c>LlmCallDescriptor.ParseToolCall</c>;
    /// the tool itself is a *description*, not a callback.</summary>
    protected override ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
        => ValueTask.FromResult<object?>(null);
}
