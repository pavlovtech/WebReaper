using System.Text.Json;
using System.Text.Json.Nodes;

namespace WebReaper.Serialization.Codecs;

/// <summary>
/// Read-side helpers for one materialized closed-sum arm (ADR-0077). A thin
/// readonly struct over the <see cref="JsonObject"/> that
/// <see cref="ClosedSumCodec{T}.Read"/> parsed once from the
/// <c>ref struct</c> <see cref="Utf8JsonReader"/>; each arm's
/// <c>build</c> delegate reads its fields through these helpers instead of
/// hand-draining the reader. Carries the sum name and the resolved tag so the
/// missing-field message reads exactly as the hand-written codecs did
/// (<c>"&lt;Sum&gt; '&lt;tag&gt;' is missing required '&lt;field&gt;'"</c>).
/// </summary>
internal readonly struct ArmReaderContext
{
    private readonly JsonObject _obj;
    private readonly string _sumName;
    private readonly string _tag;

    internal ArmReaderContext(JsonObject obj, string sumName, string tag)
    {
        _obj = obj;
        _sumName = sumName;
        _tag = tag;
    }

    /// <summary>The whole materialized arm object, for arms that need raw access.</summary>
    public JsonObject Object => _obj;

    /// <summary>A required string field; throws the shared missing-field
    /// message when the field is absent or JSON null.</summary>
    public string Require(string field)
        => _obj[field]?.GetValue<string>() ?? throw Missing(field);

    /// <summary>An optional string field; <c>null</c> when absent.</summary>
    public string? OptionalString(string field)
        => _obj[field]?.GetValue<string>();

    /// <summary>An optional integer field; <paramref name="fallback"/> when
    /// absent (matching the hand-written codecs' zero-initialised locals).</summary>
    public int OptionalInt(string field, int fallback = 0)
        => _obj[field] is { } n ? n.GetValue<int>() : fallback;

    /// <summary>The common pre-discriminator field every arm of the sum shares
    /// (AgentDecision's <c>reason</c>); empty string when absent, matching the
    /// hand-written <c>reason ??= ""</c>.</summary>
    public string Common(string field)
        => _obj[field]?.GetValue<string>() ?? "";

    /// <summary>A required nested value, built from its already-materialized
    /// child node via <paramref name="from"/> (a migrated codec's <c>From</c>
    /// or the bespoke <c>SchemaCodec.From</c>) — the composition seam. Throws
    /// the shared missing-field message when the field is absent.</summary>
    public TChild RequireChild<TChild>(string field, Func<JsonNode, TChild> from)
        => _obj[field] is { } node ? from(node) : throw Missing(field);

    /// <summary>An optional nested value; <c>null</c> when absent or JSON null.
    /// The arm supplies its own message if it treats absence as an error (the
    /// <c>ActDispatched.resolvedAction</c> bespoke message).</summary>
    public TChild? OptionalChild<TChild>(string field, Func<JsonNode, TChild> from)
        where TChild : class
        => _obj[field] is { } node && node.GetValueKind() != JsonValueKind.Null
            ? from(node)
            : null;

    /// <summary>An optional object field, returned as a detached deep clone so
    /// it carries no parent — matching the fresh-node semantics of the
    /// hand-written <c>JsonNode.Parse</c> on a streamed payload.</summary>
    public JsonObject? OptionalObjectClone(string field)
        => _obj[field] is JsonObject o ? (JsonObject)o.DeepClone() : null;

    private JsonException Missing(string field)
        => new($"{_sumName} '{_tag}' is missing required '{field}'");
}

/// <summary>
/// The one hand-written-but-shared JSON mechanism the flat closed sums
/// (<c>PageAction</c>, <c>AgentDecision</c>, <c>AgentDecisionOutcome</c>)
/// describe their arms to (ADR-0077, ADR-0008's reflection-free posture). The
/// serialization-layer sibling of the <c>WebReaper.AI</c> <c>LlmCall&lt;T&gt;</c>
/// mechanism.
/// <para>
/// The mechanism owns the object envelope, the <c>type</c> discriminator (write
/// + dispatch), the one-time <see cref="JsonNode.Parse(ref Utf8JsonReader, JsonNodeOptions?)"/>
/// materialization (the <c>ref struct</c> reader can't be handed to a per-arm
/// delegate, so the read side materializes once then dispatches on a plain
/// <see cref="JsonObject"/>), the single missing-field contract (via
/// <see cref="ArmReaderContext"/>), the unknown-tag throw, and the optional
/// common-field pass. Each arm's descriptor owns only its tag, its field
/// writes, and its build-from-object.
/// </para>
/// <para>
/// AOT-clean (<c>System.Text.Json.Nodes</c> only, no reflection). The
/// materialize-then-dispatch allocates one <see cref="JsonObject"/> per value;
/// irrelevant at these call frequencies (config load, agent resume, per agent
/// step). Bespoke codecs (<c>Schema</c>, <c>AgentRunSnapshot</c>, the
/// <c>ImmutableQueue</c> chains) compose with this via the migrated codecs'
/// <see cref="From"/> entry or their streaming <see cref="Read"/> shim.
/// </para>
/// </summary>
/// <typeparam name="T">The closed-sum base type.</typeparam>
internal sealed class ClosedSumCodec<T> where T : class
{
    /// <summary>One arm's descriptor: its wire tag, its CLR type, how to write
    /// its own fields, and how to build it from the materialized object.</summary>
    internal sealed class ArmDescriptor
    {
        /// <summary>The <c>type</c> discriminator value for this arm.</summary>
        public required string Tag { get; init; }

        /// <summary>The concrete arm CLR type, the write-dispatch key.</summary>
        public required Type ArmType { get; init; }

        /// <summary>Writes only the arm's own fields (the mechanism writes the
        /// envelope, any common field, and the type tag).</summary>
        public required Action<Utf8JsonWriter, T> WriteFields { get; init; }

        /// <summary>Builds the arm from the materialized object.</summary>
        public required Func<ArmReaderContext, T> Build { get; init; }
    }

    /// <summary>Describe one arm. <typeparamref name="TArm"/> is the concrete
    /// sealed arm; <paramref name="write"/> writes only the arm's own fields;
    /// <paramref name="build"/> constructs the arm from the materialized object
    /// via the <see cref="ArmReaderContext"/> helpers.</summary>
    public static ArmDescriptor Arm<TArm>(
        string tag,
        Action<Utf8JsonWriter, TArm> write,
        Func<ArmReaderContext, TArm> build) where TArm : T
        => new()
        {
            Tag = tag,
            ArmType = typeof(TArm),
            WriteFields = (w, v) => write(w, (TArm)v),
            Build = ctx => build(ctx),
        };

    /// <summary>Field-less arm: a one-liner for arms that carry no fields and
    /// no common field (PageAction's <c>ScrollToEnd</c> / <c>WaitForNetworkIdle</c>,
    /// AgentDecisionOutcome's <c>None</c>). Writes the tag only and constructs
    /// via <paramref name="create"/>.</summary>
    public static ArmDescriptor Arm<TArm>(string tag, Func<TArm> create) where TArm : T
        => new()
        {
            Tag = tag,
            ArmType = typeof(TArm),
            WriteFields = (_, _) => { },
            Build = _ => create(),
        };

    private readonly string _sumName;
    private readonly Action<Utf8JsonWriter, T>? _writeCommon;
    private readonly Dictionary<Type, ArmDescriptor> _byType;
    private readonly Dictionary<string, ArmDescriptor> _byTag;

    /// <param name="sumName">The sum's name, used verbatim in error messages
    /// (<c>"PageAction"</c>, <c>"AgentDecision"</c>, <c>"AgentDecisionOutcome"</c>).</param>
    /// <param name="arms">One descriptor per arm.</param>
    /// <param name="writeCommon">Optional fields written after StartObject and
    /// BEFORE the type tag — AgentDecision's <c>reason</c>. Byte order is
    /// load-bearing; this is how reason-before-type is preserved.</param>
    public ClosedSumCodec(
        string sumName,
        IReadOnlyList<ArmDescriptor> arms,
        Action<Utf8JsonWriter, T>? writeCommon = null)
    {
        _sumName = sumName;
        _writeCommon = writeCommon;
        _byType = arms.ToDictionary(a => a.ArmType);
        _byTag = arms.ToDictionary(a => a.Tag, StringComparer.Ordinal);
    }

    /// <summary>Write <paramref name="value"/> as
    /// <c>{ [common…,] "type": tag, fields… }</c>.</summary>
    public void Write(Utf8JsonWriter w, T value)
    {
        w.WriteStartObject();
        _writeCommon?.Invoke(w, value);
        if (!_byType.TryGetValue(value.GetType(), out var arm))
            throw new JsonException($"unhandled {_sumName} arm '{value.GetType().Name}'");
        w.WriteString("type", arm.Tag);
        arm.WriteFields(w, value);
        w.WriteEndObject();
    }

    /// <summary>Streaming entry: materialize one value from the reader and
    /// dispatch. The <c>ref struct</c> reader is consumed once here. Leaves the
    /// reader positioned on the value's closing token, exactly like a
    /// hand-drained loop — so streaming consumers (the selector chain, the
    /// agent snapshot) read a sequence of these unchanged.</summary>
    public T Read(ref Utf8JsonReader r)
        => From(JsonNode.Parse(ref r) ?? throw new JsonException("expected object"));

    /// <summary>Materialized entry: build the arm from an already-parsed node —
    /// the composition seam a parent arm uses for a nested child.</summary>
    public T From(JsonNode node)
    {
        var obj = node as JsonObject ?? throw new JsonException("expected object");
        var tag = obj["type"]?.GetValue<string>();
        if (tag is null || !_byTag.TryGetValue(tag, out var arm))
            throw new JsonException($"unknown {_sumName} type '{tag}'");
        return arm.Build(new ArmReaderContext(obj, _sumName, tag));
    }
}
