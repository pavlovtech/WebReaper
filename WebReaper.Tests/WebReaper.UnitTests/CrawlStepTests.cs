using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Core.Crawling;
using WebReaper.Core.Crawling.Concrete;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Domain;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;

namespace WebReaper.UnitTests;

// The crawl-step state machine, tested directly through its interface — no
// Spider, no loaders, no tracker, no network. Each test is "selector chain +
// HTML ⇒ assert the outcome arm and its selector-chain handling".
public class CrawlStepTests
{
    private static CrawlStep Step() =>
        new(new LinkParserByCssSelector(), new AngleSharpContentParser(NullLogger.Instance));

    private static Job Job(string url, params LinkPathSelector[] chain) =>
        new(url, ImmutableQueue.CreateRange(chain), ImmutableQueue.Create<string>());

    [Fact]
    public async Task Empty_chain_yields_one_ParsedData_and_no_jobs()
    {
        var job = Job("https://x.test/p/1");                       // 0 selectors ⇒ target
        var schema = new Schema { new("title", "h1.t") };
        const string html = "<html><body><h1 class='t'>Hello</h1></body></html>";

        var outcome = await Step().StepAsync(job, html, schema);

        var parsed = Assert.IsType<CrawlOutcome.Parsed>(outcome);
        Assert.Equal("https://x.test/p/1", parsed.Data.Url);
        Assert.Equal("Hello", parsed.Data.Data["title"]?.ToString());
        Assert.Empty(outcome.NextJobs);
    }

    [Fact]
    public async Task Transit_page_advances_the_selector_chain_on_every_child()
    {
        var head = new LinkPathSelector("a.item");                 // transit (no pagination)
        var tail = new LinkPathSelector("a.detail");
        var job = Job("https://x.test/", head, tail);
        const string html = "<html><body>" +
                            "<a class='item' href='/a'>a</a>" +
                            "<a class='item' href='/b'>b</a></body></html>";

        var outcome = await Step().StepAsync(job, html, null);

        var followed = Assert.IsType<CrawlOutcome.Followed>(outcome);
        Assert.Equal(new[] { "https://x.test/a", "https://x.test/b" },
            followed.Next.Select(j => j.Url));
        Assert.All(followed.Next, j =>
        {
            // advanced: the head selector was consumed, only the tail remains
            Assert.Equal(tail, Assert.Single(j.LinkPathSelectors));
            Assert.Equal("https://x.test/", j.ParentBacklinks.Single()); // provenance
        });
    }

    [Fact]
    public async Task Pagination_advances_items_but_retains_chain_for_next_pages()
    {
        var head = new LinkPathSelector("a.item", PaginationSelector: "a.next");
        var job = Job("https://x.test/list", head);                // 1 selector + pagination
        const string html = "<html><body>" +
                            "<a class='item' href='/i1'>i1</a>" +
                            "<a class='next' href='/list?p=2'>2</a></body></html>";

        var outcome = await Step().StepAsync(job, html, null);

        var paginated = Assert.IsType<CrawlOutcome.Paginated>(outcome);

        var item = Assert.Single(paginated.Items);
        Assert.Equal("https://x.test/i1", item.Url);
        Assert.Empty(item.LinkPathSelectors);                       // advanced ⇒ target page

        var nextPage = Assert.Single(paginated.NextPages);
        Assert.Equal("https://x.test/list?p=2", nextPage.Url);
        var retained = Assert.Single(nextPage.LinkPathSelectors);   // retained ⇒ same step
        Assert.Equal(head, retained);
        Assert.True(retained.HasPagination);

        // NextJobs projection = items then next-pages, in order.
        Assert.Equal(new[] { "https://x.test/i1", "https://x.test/list?p=2" },
            outcome.NextJobs.Select(j => j.Url));
    }
}
