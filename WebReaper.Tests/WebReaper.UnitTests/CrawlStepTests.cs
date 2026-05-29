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
    private static CrawlStep Step(SweepPolicy? sweepPolicy = null) =>
        new(new SchemaFold<AngleSharp.Dom.IParentNode>(new AngleSharpSchemaBackend(), NullLogger.Instance),
            sweepPolicy);

    private static Job Job(string url, params LinkPathSelector[] chain) =>
        new(url, ImmutableQueue.CreateRange(chain), ImmutableQueue.Create<string>());

    private static Job JobWithBacklinks(string url, string[] backlinks, params LinkPathSelector[] chain) =>
        new(url, ImmutableQueue.CreateRange(chain), ImmutableQueue.CreateRange(backlinks));

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
    public async Task Transit_page_skips_anchors_with_no_usable_href()
    {
        // An <a> matching the selector but with no usable href — absent or
        // empty — is skipped, not a crash. Pre-ADR-0036 the missing href
        // reached `new Uri(baseUrl, null)` and threw ArgumentNullException
        // mid-step.
        var job = Job("https://x.test/", new LinkPathSelector("a.item"));
        const string html = "<html><body>" +
                            "<a class='item' href='/a'>a</a>" +
                            "<a class='item'>no href</a>" +
                            "<a class='item' href=''>empty href</a></body></html>";

        var outcome = await Step().StepAsync(job, html, null);

        var followed = Assert.IsType<CrawlOutcome.Followed>(outcome);
        Assert.Equal(new[] { "https://x.test/a" }, followed.Next.Select(j => j.Url));
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

    // ---- Sweep page (ADR-0081): the one arm that extracts AND follows ----

    [Fact]
    public async Task Sweep_page_yields_ParsedData_and_on_domain_children_that_retain_the_sweep_selector()
    {
        var sweep = LinkPathSelector.Sweep();                      // recursive a[href]
        var job = Job("https://x.test/", sweep);
        var schema = new Schema { new("title", "h1.t") };
        const string html = "<html><body><h1 class='t'>Home</h1>" +
                            "<a href='/a'>a</a>" +
                            "<a href='/b'>b</a>" +
                            "<a href='https://other.test/x'>off</a></body></html>";

        var outcome = await Step().StepAsync(job, html, schema);

        var swept = Assert.IsType<CrawlOutcome.Swept>(outcome);
        // Extracts like a target page...
        Assert.Equal("https://x.test/", swept.Data.Url);
        Assert.Equal("Home", swept.Data.Data["title"]?.ToString());
        // ...AND follows its on-domain links (the off-domain one is dropped).
        Assert.Equal(new[] { "https://x.test/a", "https://x.test/b" },
            swept.Next.Select(j => j.Url));
        Assert.All(swept.Next, j =>
        {
            // RETAIN: the child carries the same one-element recursive chain.
            var retained = Assert.Single(j.LinkPathSelectors);
            Assert.True(retained.Recursive);
            Assert.Equal(sweep, retained);
            Assert.Equal("https://x.test/", j.ParentBacklinks.Single()); // provenance
        });
    }

    [Fact]
    public async Task Sweep_excludes_subdomains_by_default_but_includes_them_when_opted_in()
    {
        var job = Job("https://example.com/", LinkPathSelector.Sweep());
        const string html = "<html><body>" +
                            "<a href='https://blog.example.com/x'>sub</a></body></html>";
        var schema = new Schema { new("title", "h1") };

        // Default (no policy ⇒ anchor derived from the page host, no subdomains).
        var byDefault = Assert.IsType<CrawlOutcome.Swept>(
            await Step().StepAsync(job, html, schema));
        Assert.Empty(byDefault.Next);

        // --include-subdomains widens to the apex suffix.
        var opted = Assert.IsType<CrawlOutcome.Swept>(
            await Step(new SweepPolicy("example.com", IncludeSubdomains: true, MaxDepth: int.MaxValue))
                .StepAsync(job, html, schema));
        Assert.Equal(new[] { "https://blog.example.com/x" }, opted.Next.Select(j => j.Url));
    }

    [Fact]
    public async Task Sweep_stops_following_at_max_depth_but_still_extracts_the_page()
    {
        var sweep = LinkPathSelector.Sweep();
        var schema = new Schema { new("title", "h1") };
        const string html = "<html><body><h1>deep</h1><a href='/c'>c</a></body></html>";

        var policy = new SweepPolicy("x.test", IncludeSubdomains: false, MaxDepth: 1);

        // Depth 1 (one backlink) with MaxDepth 1: extract, do NOT follow.
        var atCap = JobWithBacklinks("https://x.test/b", new[] { "https://x.test/" }, sweep);
        var capped = Assert.IsType<CrawlOutcome.Swept>(await Step(policy).StepAsync(atCap, html, schema));
        Assert.Equal("deep", capped.Data.Data["title"]?.ToString());
        Assert.Empty(capped.Next);

        // Depth 0 (the start page) with MaxDepth 1: still follows.
        var atStart = Job("https://x.test/", sweep);
        var followed = Assert.IsType<CrawlOutcome.Swept>(await Step(policy).StepAsync(atStart, html, schema));
        Assert.Equal(new[] { "https://x.test/c" }, followed.Next.Select(j => j.Url));
    }
}
