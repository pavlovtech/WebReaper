using WebReaper.Builders;
using WebReaper.Core.Loaders.Abstract;

namespace WebReaper.UnitTests;

// ADR-0083 slice 5: WithLoadTransport appends a Dynamic rung rather than
// replacing the single slot, so a consumer (the CLI) registers a vanilla browser
// rung and then a stealth rung to build the HTTP -> browser -> stealth ladder.
// Each registered factory is invoked once at build time; the single-slot
// behaviour would have invoked only the last.
public class WithLoadTransportAccumulateTests
{
    private sealed class FakeTransport : IPageLoadTransport
    {
        public Task<PageLoadResult> LoadAsync(PageRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new PageLoadResult { Html = string.Empty });
    }

    [Fact]
    public async Task Every_registered_dynamic_transport_factory_is_built_into_a_rung()
    {
        var invoked = 0;

        await using var engine = await ScraperEngineBuilder
            .Crawl("http://example.test/")
            .AsMarkdown()
            .WithLoadTransport((_, _, _, _) => { Interlocked.Increment(ref invoked); return new FakeTransport(); })
            .WithLoadTransport((_, _, _, _) => { Interlocked.Increment(ref invoked); return new FakeTransport(); })
            .BuildAsync();

        // Both factories ran -> both became rungs (the old single slot would be 1).
        Assert.Equal(2, invoked);
    }
}
