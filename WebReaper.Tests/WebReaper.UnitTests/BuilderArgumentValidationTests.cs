using WebReaper.Builders;

namespace WebReaper.UnitTests;

// 8.0.0 builder argument-validation hardening. Every public builder entry
// point that takes a free-form string (start URLs, link/pagination selectors,
// file paths) used to accept null/empty/whitespace silently and fail much
// later — at parse time, at first file I/O, or (start URLs) as an engine that
// quietly does nothing. These pin the fail-fast contract: an invalid argument
// is rejected at the call that introduced it, with a clear exception. A major
// is the right window since this turns previously-"accepted" misuse into a
// throw.
public class BuilderArgumentValidationTests
{
    // ADR-0025: "build with no start URLs or no schema" is now unrepresentable
    // — the only path to a builder that can Build()/BuildAsync() is the static
    // Crawl(...).Extract(...) seed (ScraperEngineBuilder's ctor is internal).
    // The old InvalidOperationException guards in ConfigBuilder.Build are gone,
    // deleted by construction (the whole solution compiling on the seed entry
    // is the test). What remains is fail-fast on an *empty* seed.
    [Fact]
    public void Crawl_with_no_start_url_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => ScraperEngineBuilder.Crawl());
        Assert.Throws<ArgumentException>(() => ScraperEngineBuilder.CrawlWithBrowser());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Follow_and_Paginate_reject_a_blank_selector(string? selector)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ConfigBuilder().Follow(selector!));
        Assert.ThrowsAny<ArgumentException>(() => new ConfigBuilder().FollowWithBrowser(selector!));
        Assert.ThrowsAny<ArgumentException>(() => new ConfigBuilder().Paginate(selector!, "next"));
        Assert.ThrowsAny<ArgumentException>(() => new ConfigBuilder().Paginate("a.link", selector!));
        Assert.ThrowsAny<ArgumentException>(() => new ConfigBuilder().PaginateWithBrowser(selector!, "next"));
        Assert.ThrowsAny<ArgumentException>(() => new ConfigBuilder().PaginateWithBrowser("a.link", selector!));
    }

    [Fact]
    public void A_valid_selector_is_still_accepted()
    {
        var ex = Record.Exception(() => new ConfigBuilder()
            .Follow("a.link")
            .Paginate("a.item", "a.next"));

        Assert.Null(ex);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void File_backed_builder_methods_reject_a_blank_path(string? path)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ScraperEngineBuilder().WriteToCsvFile(path!, false));
        Assert.ThrowsAny<ArgumentException>(() => new ScraperEngineBuilder().WriteToJsonFile(path!));
        Assert.ThrowsAny<ArgumentException>(() => new ScraperEngineBuilder().TrackVisitedLinksInFile(path!));
        Assert.ThrowsAny<ArgumentException>(() => new ScraperEngineBuilder().WithFileConfigStorage(path!));
        Assert.ThrowsAny<ArgumentException>(() => new ScraperEngineBuilder().WithFileCookieStorage(path!));
        Assert.ThrowsAny<ArgumentException>(() => new ScraperEngineBuilder().WithTextFileScheduler(path!, "pos.txt"));
        Assert.ThrowsAny<ArgumentException>(() => new ScraperEngineBuilder().WithTextFileScheduler("jobs.txt", path!));
    }
}
