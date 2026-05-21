using System.Text.Json.Nodes;
using WebReaper.Sinks.Models;

namespace WebReaper.UnitTests;

// ADR-0031: ParsedData's construction owns the URL-merge — the page URL is
// folded into Data under "url", so every sink writes Data as-is and none
// re-merges (the duplicated line that drifted: ConsoleSink had no merge).
// These pin the merge at the construction interface.
public class ParsedDataConstructionTests
{
    [Fact]
    public void Construction_folds_the_url_into_Data()
    {
        var data = new JsonObject { ["title"] = "Hello" };

        var parsed = new ParsedData("https://x.test/p", data);

        Assert.Equal("https://x.test/p", parsed.Data["url"]!.GetValue<string>());
        Assert.Equal("Hello", parsed.Data["title"]!.GetValue<string>());
    }

    [Fact]
    public void The_typed_Url_accessor_is_preserved()
    {
        var parsed = new ParsedData("https://x.test/p", new JsonObject());

        Assert.Equal("https://x.test/p", parsed.Url);
    }

    [Fact]
    public void The_merge_is_idempotent()
    {
        // Re-constructing from an already-merged Data leaves a single "url"
        // entry holding the page URL — the merge is a set, not an append.
        var first = new ParsedData("https://x.test/a", new JsonObject());
        var second = new ParsedData("https://x.test/a", first.Data);

        Assert.Equal("https://x.test/a", second.Data["url"]!.GetValue<string>());
    }

    [Fact]
    public void A_schema_field_named_url_is_overwritten_by_the_page_url()
    {
        // "url" is a reserved key in the emitted record (ADR-0031) — unchanged
        // behaviour: the four persisting sinks already clobbered it.
        var data = new JsonObject { ["url"] = "schema-field-value" };

        var parsed = new ParsedData("https://x.test/p", data);

        Assert.Equal("https://x.test/p", parsed.Data["url"]!.GetValue<string>());
    }
}
