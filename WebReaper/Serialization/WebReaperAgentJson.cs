using System.Text;
using System.Text.Json;
using WebReaper.Domain.Agent;
using WebReaper.Serialization.Converters;

namespace WebReaper.Serialization;

/// <summary>
/// The serialization seam for agent-run state (ADR-0051 §Decision §6) — the
/// public surface every <see cref="WebReaper.Core.Agent.Abstract.IAgentRunStore"/>
/// adapter uses to round-trip an <see cref="AgentRunSnapshot"/> through its
/// backing store (file, Redis, Mongo, Sqlite, Cosmos). Hand-written
/// <see cref="Utf8JsonWriter"/> / <see cref="Utf8JsonReader"/> via
/// <see cref="AgentRunSnapshotCodec"/> — AOT-trivially-safe and self-contained
/// (no source-gen context dependency, since the snapshot's polymorphic
/// <see cref="AgentDecision"/> arms and its
/// <see cref="System.Text.Json.Nodes.JsonObject"/> record payloads are awkward
/// inside the existing <see cref="WebReaperJsonContext"/>).
/// </summary>
public static class WebReaperAgentJson
{
    /// <summary>Serialize <paramref name="snapshot"/> to UTF-8 JSON text.</summary>
    public static string SerializeSnapshot(AgentRunSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            AgentRunSnapshotCodec.Write(writer, snapshot);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Parse an <see cref="AgentRunSnapshot"/> from JSON text.</summary>
    public static AgentRunSnapshot DeserializeSnapshot(string json)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);
        var bytes = Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(bytes);
        if (!reader.Read()) throw new JsonException("empty agent snapshot payload");
        return AgentRunSnapshotCodec.Read(ref reader);
    }
}
