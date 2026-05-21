using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using WebReaper.Domain.PageActions;
using WebReaper.Domain.Selectors;

namespace WebReaper.Serialization.Converters;

/// <summary>
/// <c>ImmutableQueue&lt;LinkPathSelector&gt;</c> codec (ADR 0008) — the
/// selector chain on <see cref="WebReaper.Domain.ScraperConfig"/> and
/// <see cref="WebReaper.Domain.Job"/>. STJ has no AOT-safe built-in for
/// <c>ImmutableQueue&lt;T&gt;</c>; serialise as an array and rebuild with
/// <see cref="ImmutableQueue.CreateRange{T}"/>. <see cref="LinkPathSelector"/>
/// is written fully manually (no nested serializer call) so the AOT signal
/// stays unambiguous; its <c>PageActions</c> reuse <see cref="PageActionCodec"/>.
/// Two concrete converters rather than a generic factory: a
/// <c>JsonConverterFactory</c> over <c>MakeGenericType</c> is the AOT hazard
/// this whole ADR exists to remove.
/// </summary>
internal sealed class SelectorChainJsonConverter : JsonConverter<ImmutableQueue<LinkPathSelector>>
{
    public override ImmutableQueue<LinkPathSelector> Read(
        ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("expected array");
        var items = new List<LinkPathSelector>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            items.Add(ReadSelector(ref reader));
        return ImmutableQueue.CreateRange(items);
    }

    public override void Write(
        Utf8JsonWriter writer, ImmutableQueue<LinkPathSelector> value, JsonSerializerOptions o)
    {
        writer.WriteStartArray();
        foreach (var s in value) WriteSelector(writer, s);
        writer.WriteEndArray();
    }

    private static void WriteSelector(Utf8JsonWriter w, LinkPathSelector s)
    {
        w.WriteStartObject();
        w.WriteString("selector", s.Selector);
        if (s.PaginationSelector is not null) w.WriteString("paginationSelector", s.PaginationSelector);
        w.WriteString("pageType", s.PageType.ToString());
        if (s.PageActions is { } acts)
        {
            w.WritePropertyName("pageActions");
            w.WriteStartArray();
            foreach (var a in acts) PageActionCodec.Write(w, a);
            w.WriteEndArray();
        }
        w.WriteEndObject();
    }

    private static LinkPathSelector ReadSelector(ref Utf8JsonReader r)
    {
        if (r.TokenType != JsonTokenType.StartObject) throw new JsonException("expected object");
        string sel = "";
        string? pag = null;
        PageType pt = PageType.Static;
        List<PageAction>? acts = null;
        while (r.Read() && r.TokenType != JsonTokenType.EndObject)
        {
            var prop = r.GetString();
            r.Read();
            switch (prop)
            {
                case "selector": sel = r.GetString() ?? ""; break;
                case "paginationSelector": pag = r.GetString(); break;
                case "pageType":
                    pt = r.GetString() == "Dynamic" ? PageType.Dynamic : PageType.Static;
                    break;
                case "pageActions":
                    acts = new List<PageAction>();
                    while (r.Read() && r.TokenType != JsonTokenType.EndArray)
                        acts.Add(PageActionCodec.Read(ref r));
                    break;
                default: r.Skip(); break;
            }
        }
        // ADR-0030: a corrupt persisted Job — a chain entry missing its
        // 'selector' — fails fast here at queue-read with the JSON property
        // name, not late at the Crawl step. The LinkPathSelector ctor
        // enforces the other two grammar rules (empty paginationSelector,
        // PageActions with a static transport).
        if (string.IsNullOrWhiteSpace(sel))
            throw new JsonException("missing or empty 'selector' on a LinkPathSelector entry");

        return new LinkPathSelector(sel, pag, pt, acts);
    }
}

/// <summary>
/// <c>ImmutableQueue&lt;string&gt;</c> codec — <see cref="WebReaper.Domain.Job"/>'s
/// <c>ParentBacklinks</c>. Same array + <see cref="ImmutableQueue.CreateRange{T}"/>
/// shape as the selector-chain converter; closes the other half of the ADR-0005
/// Job round-trip.
/// </summary>
internal sealed class BacklinkQueueJsonConverter : JsonConverter<ImmutableQueue<string>>
{
    public override ImmutableQueue<string> Read(
        ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("expected array");
        var items = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            items.Add(reader.GetString() ?? "");
        return ImmutableQueue.CreateRange(items);
    }

    public override void Write(
        Utf8JsonWriter writer, ImmutableQueue<string> value, JsonSerializerOptions o)
    {
        writer.WriteStartArray();
        foreach (var s in value) writer.WriteStringValue(s);
        writer.WriteEndArray();
    }
}
