using WebReaper.Domain;
using WebReaper.Domain.PageActions;
using WebReaper.Domain.Selectors;

namespace WebReaper.UnitTests;

// ADR-0030: the selector-chain grammar is enforced at the LinkPathSelector
// construction site — the primary constructor rejects an empty Selector, an
// empty (non-null) PaginationSelector, and PageActions carrying actions when
// PageType is Static; Follow / Paginate are the named factories for the two
// intent-shapes. These tests pin every rule at the construction interface —
// no builder, no parser, no crawl.
public class LinkPathSelectorConstructionTests
{
    // ---- Selector non-empty ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_rejects_an_empty_selector(string selector)
    {
        Assert.Throws<ArgumentException>(() => new LinkPathSelector(selector));
    }

    [Fact]
    public void Constructor_rejects_a_null_selector()
    {
        Assert.Throws<ArgumentNullException>(() => new LinkPathSelector(null!));
    }

    [Fact]
    public void Constructor_accepts_a_well_formed_selector()
    {
        var s = new LinkPathSelector("a.item");

        Assert.Equal("a.item", s.Selector);
        Assert.Null(s.PaginationSelector);
        Assert.False(s.HasPagination);
    }

    // ---- PaginationSelector non-empty when present ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_rejects_an_empty_pagination_selector(string pagination)
    {
        Assert.Throws<ArgumentException>(
            () => new LinkPathSelector("a.item", pagination));
    }

    [Fact]
    public void Constructor_accepts_a_null_pagination_selector()
    {
        var s = new LinkPathSelector("a.item", null);

        Assert.Null(s.PaginationSelector);
        Assert.False(s.HasPagination);
    }

    // ---- PageActions content iff PageType.Dynamic ----

    [Fact]
    public void Constructor_rejects_page_actions_with_a_static_transport()
    {
        var actions = new List<PageAction> { new(PageActionType.Click, ".accept") };

        var ex = Assert.Throws<ArgumentException>(
            () => new LinkPathSelector("a.item", null, PageType.Static, actions));
        Assert.Contains("Dynamic", ex.Message);
    }

    [Fact]
    public void Constructor_accepts_an_empty_page_actions_list_with_a_static_transport()
    {
        // Empty equals absent (ADR-0030, mirroring ADR-0028) — there is no
        // silent feature-drop because there is nothing to drop.
        var s = new LinkPathSelector(
            "a.item", null, PageType.Static, new List<PageAction>());

        Assert.Empty(s.PageActions!);
    }

    [Fact]
    public void Constructor_accepts_page_actions_with_a_dynamic_transport()
    {
        var actions = new List<PageAction> { new(PageActionType.Click, ".accept") };

        var s = new LinkPathSelector("a.item", null, PageType.Dynamic, actions);

        Assert.Single(s.PageActions!);
    }

    // ---- Follow / Paginate factories ----

    [Fact]
    public void Follow_builds_a_no_pagination_step()
    {
        var s = LinkPathSelector.Follow("a.item");

        Assert.Equal("a.item", s.Selector);
        Assert.Null(s.PaginationSelector);
        Assert.False(s.HasPagination);
        Assert.Equal(PageType.Static, s.PageType);
    }

    [Fact]
    public void Paginate_builds_a_pagination_step()
    {
        var s = LinkPathSelector.Paginate("a.item", "a.next");

        Assert.Equal("a.item", s.Selector);
        Assert.Equal("a.next", s.PaginationSelector);
        Assert.True(s.HasPagination);
    }

    [Fact]
    public void Follow_rejects_an_empty_selector()
    {
        Assert.Throws<ArgumentException>(() => LinkPathSelector.Follow(""));
    }

    [Fact]
    public void Paginate_rejects_an_empty_item_selector()
    {
        Assert.Throws<ArgumentException>(
            () => LinkPathSelector.Paginate("", "a.next"));
    }

    [Fact]
    public void Paginate_rejects_an_empty_pagination_selector()
    {
        Assert.Throws<ArgumentException>(
            () => LinkPathSelector.Paginate("a.item", ""));
    }

    [Fact]
    public void Paginate_rejects_a_null_pagination_selector()
    {
        // The Paginate factory carries the paginate intent — a null pagination
        // selector is malformed there, even though the primary constructor
        // allows null (that is the Follow shape, see the test above).
        Assert.Throws<ArgumentNullException>(
            () => LinkPathSelector.Paginate("a.item", null!));
    }

    [Fact]
    public void Follow_with_browser_actions_uses_a_dynamic_transport()
    {
        var actions = new List<PageAction> { new(PageActionType.Click, ".accept") };

        var s = LinkPathSelector.Follow("a.item", PageType.Dynamic, actions);

        Assert.Equal(PageType.Dynamic, s.PageType);
        Assert.Single(s.PageActions!);
    }
}
