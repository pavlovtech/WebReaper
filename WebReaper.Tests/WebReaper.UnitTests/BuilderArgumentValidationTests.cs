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
    [Fact]
    public void Build_without_start_urls_reports_them_in_the_plural()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new ConfigBuilder().Build());

        // The method takes params string[]; the message must not say "Url is".
        Assert.Contains("Start URLs", ex.Message);
    }

    [Fact]
    public void Build_with_an_empty_start_url_set_is_rejected()
    {
        // Get() with zero URLs previously built a config that crawled nothing.
        // (The start-URL check runs before the schema check in Build().)
        Assert.Throws<InvalidOperationException>(
            () => new ConfigBuilder().Get().Build());
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
