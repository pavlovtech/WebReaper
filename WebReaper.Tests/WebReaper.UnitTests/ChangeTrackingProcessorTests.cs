using System.Text.Json.Nodes;
using WebReaper.Domain.Parsing;
using WebReaper.Processing;
using WebReaper.Processing.Concrete;
using WebReaper.Sinks.Models;

namespace WebReaper.UnitTests;

// ADR-0048: the change-tracking page processor. Tests pin the new →
// same → changed transition across two visits to the same URL, plus
// the in-memory store's per-URL keying.
public class ChangeTrackingProcessorTests
{
    private const string Url = "https://x.test/page";

    [Fact]
    public async Task First_visit_to_a_url_is_marked_new()
    {
        var processor = new ChangeTrackingProcessor(new InMemoryChangeStore());

        var verdict = await processor.ProcessAsync(MakeContext(Url, "<article><h1>Hi</h1></article>"), default);

        var kept = Assert.IsType<PageVerdict.Kept>(verdict);
        Assert.Equal("new", kept.Data.Data[ChangeTrackingProcessor.StatusKey]!.GetValue<string>());
        Assert.False(kept.Data.Data.ContainsKey(ChangeTrackingProcessor.PreviousHashKey));
    }

    [Fact]
    public async Task Second_visit_with_same_content_is_marked_same()
    {
        var processor = new ChangeTrackingProcessor(new InMemoryChangeStore());
        const string html = "<article><h1>Hi</h1></article>";

        await processor.ProcessAsync(MakeContext(Url, html), default);
        var second = await processor.ProcessAsync(MakeContext(Url, html), default);

        var kept = Assert.IsType<PageVerdict.Kept>(second);
        Assert.Equal("same", kept.Data.Data[ChangeTrackingProcessor.StatusKey]!.GetValue<string>());
        Assert.True(kept.Data.Data.ContainsKey(ChangeTrackingProcessor.PreviousHashKey));
    }

    [Fact]
    public async Task Second_visit_with_different_content_is_marked_changed()
    {
        var processor = new ChangeTrackingProcessor(new InMemoryChangeStore());

        await processor.ProcessAsync(
            MakeContext(Url, "<article><h1>Original</h1></article>"), default);
        var second = await processor.ProcessAsync(
            MakeContext(Url, "<article><h1>Different</h1></article>"), default);

        var kept = Assert.IsType<PageVerdict.Kept>(second);
        Assert.Equal("changed", kept.Data.Data[ChangeTrackingProcessor.StatusKey]!.GetValue<string>());
        Assert.True(kept.Data.Data.ContainsKey(ChangeTrackingProcessor.PreviousHashKey));
    }

    [Fact]
    public async Task Different_urls_have_independent_hashes()
    {
        var processor = new ChangeTrackingProcessor(new InMemoryChangeStore());

        await processor.ProcessAsync(MakeContext("https://x.test/a", "<h1>A</h1>"), default);
        var second = await processor.ProcessAsync(
            MakeContext("https://x.test/b", "<h1>B</h1>"), default);

        // The b URL was never seen; "new" despite the a's prior write.
        var kept = Assert.IsType<PageVerdict.Kept>(second);
        Assert.Equal("new", kept.Data.Data[ChangeTrackingProcessor.StatusKey]!.GetValue<string>());
    }

    [Fact]
    public async Task Whitespace_only_changes_are_invisible_to_the_hash()
    {
        // Markdown-based hashing strips template noise; whitespace
        // tweaks to the HTML should NOT flip the status to changed.
        var processor = new ChangeTrackingProcessor(new InMemoryChangeStore());

        await processor.ProcessAsync(
            MakeContext(Url, "<article><h1>Hello</h1></article>"), default);
        var second = await processor.ProcessAsync(
            MakeContext(Url, "<article>  <h1>Hello</h1>  </article>"), default);

        // Same Markdown output → same hash → "same" status.
        var kept = Assert.IsType<PageVerdict.Kept>(second);
        Assert.Equal("same", kept.Data.Data[ChangeTrackingProcessor.StatusKey]!.GetValue<string>());
    }

    [Fact]
    public void Constructor_rejects_null_store()
    {
        Assert.Throws<ArgumentNullException>(() => new ChangeTrackingProcessor(null!));
    }

    private static PageContext MakeContext(string url, string html) =>
        new(
            Data: new ParsedData(url, new JsonObject()),
            Html: html,
            BackLinks: Array.Empty<string>(),
            Schema: null);
}
